using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XML;

namespace NativeCompiler;

// FEATURE: MSBuild <AssemblyAttribute> items -> [assembly:] attributes.
//
// The SDK turns `@(AssemblyAttribute)` items into generated `[assembly:]` source (WriteCodeFragment).
// Items come from two places:
//   1. Static declarations in the project (or its imported .props/.targets) - handled directly.
//   2. Targets that run during the build and inject items - e.g. a "module manifest" target that
//      embeds the referenced projects as `[assembly: ...ModuleNameAttribute("<name>")]` so they can
//      be discovered at startup.
//
// sharp does not run MSBuild targets, so this evaluator emulates the small, bounded subset of MSBuild
// needed for (2) - and ONLY when an imported file actually declares an `<AssemblyAttribute>` inside a
// `<Target>`. It contains no product-specific strings: the attribute type, target names, item names
// and the module/"target output" wiring are all read from the .targets/.props files.
//
// Emulated subset: <Target>/<PropertyGroup>/<ItemGroup> ordering, <MSBuild Targets= Projects=>
// task with <Output TaskParameter="TargetOutputs">, target Returns, `$(prop)` / `@(item)` /
// `%(item.metadata)` expansion (the latter batches one attribute per item), the well-known
// `_MSBuildProjectReferenceExistent` item, and simple `==/!=/Exists()/and/or/!` conditions.
// Anything unrecognized degrades to "no target-produced attributes" (never throws).
//
// To DELETE the feature: remove this file and the call in NativeCompilerUtils.GetProjectDetails.
public static class AssemblyAttributeEvaluator
{
    public static IReadOnlyList<string> GetAttributeSources(Project project)
    {
        var lines = new List<string>();

        // Static items already parsed from the project's own ItemGroups.
        foreach (var attribute in project.AssemblyAttributes)
        {
            var line = Format(attribute.Include, attribute.PositionalParameters, attribute.NamedParameters);
            if (line is not null) lines.Add(line);
        }

        try
        {
            var cache = new Dictionary<string, Graph>(StringComparer.OrdinalIgnoreCase);
            var app = Graph.Load(project.CsProjFile.FilePath, cache);

            // Static <AssemblyAttribute> items declared by imported .props/.targets.
            foreach (var (type, positional, named) in ReadAttributeItems(app.RootItems))
            {
                var line = Format(type, positional, named);
                if (line is not null) lines.Add(line);
            }

            // Items injected by targets (e.g. a module manifest target).
            var projectReferences = TransitiveProjectReferences(project.CsProjFile.FilePath);
            foreach (var target in app.Targets.Values)
            {
                if (!ContainsAssemblyAttribute(target)) continue;
                foreach (var (type, positional, named) in app.EvaluateTarget(target, projectReferences, cache).Attributes)
                {
                    var line = Format(type, positional, named);
                    if (line is not null) lines.Add(line);
                }
            }
        }
        catch
        {
            // Fail-safe: any evaluation problem degrades to the static-only behavior.
        }

        return lines.Distinct(StringComparer.Ordinal).ToList();
    }

    // The same bounded MSBuild evaluation also produces the final `@(EmbeddedResource)` item set for
    // the module-asset targets (OrchardCore's `OrchardCoreEmbedModuleAssets` and any equivalent),
    // each carrying a `LogicalName`. sharp embeds those files verbatim (see NativeCompiler.ProduceDll)
    // so the compiled assembly can serve them the way `dotnet build` does. Items are only read from
    // targets that also declare an `<AssemblyAttribute>` (the same gate as attributes), and .resx is
    // skipped because the SDK compiles those through `GenerateResource`, which sharp does not emulate.
    public static IReadOnlyList<(string Name, string FilePath)> GetEmbeddedResources(Project project)
    {
        var resources = new List<(string Name, string FilePath)>();

        try
        {
            var cache = new Dictionary<string, Graph>(StringComparer.OrdinalIgnoreCase);
            var app = Graph.Load(project.CsProjFile.FilePath, cache);
            var projectReferences = TransitiveProjectReferences(project.CsProjFile.FilePath);

            foreach (var target in app.Targets.Values)
            {
                if (!ContainsAssemblyAttribute(target)) continue;
                if (!target.Descendants().Any(element => element.Name.LocalName == "EmbeddedResource")) continue;

                var evaluated = app.EvaluateTarget(target, projectReferences, cache);
                if (!evaluated.Items.TryGetValue("EmbeddedResource", out var embeddedResources)) continue;

                foreach (var item in embeddedResources)
                {
                    if (!item.TryGetValue("FullPath", out var fullPath) || !File.Exists(fullPath)) continue;

                    var extension = item.GetValueOrDefault("Extension") ?? Path.GetExtension(fullPath);
                    if (string.Equals(extension, ".resx", StringComparison.OrdinalIgnoreCase)) continue;

                    var name = item.GetValueOrDefault("LogicalName");
                    if (string.IsNullOrEmpty(name)) name = DefaultResourceName(app, fullPath);

                    resources.Add((name, fullPath));
                }
            }
        }
        catch
        {
            // Fail-safe: never let resource discovery break the build.
        }

        return resources
            .GroupBy(resource => resource.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static string DefaultResourceName(Graph app, string fullPath)
    {
        var root = app.Props.GetValueOrDefault("RootNamespace")
                   ?? app.Props.GetValueOrDefault("AssemblyName")
                   ?? "";
        var relative = Path.GetRelativePath(app.Directory, fullPath).Replace('\\', '.');
        return root.Length == 0 ? relative : root + "." + relative;
    }

    private static bool ContainsAssemblyAttribute(XElement target) =>
        target.Descendants().Any(element => element.Name.LocalName == "AssemblyAttribute");

    private static string? Format(string? type, IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;

        var arguments = new List<string>();
        foreach (var value in positional) arguments.Add(NativeCompilerUtils.CSharpStringLiteral(value));
        foreach (var (name, value) in named) arguments.Add($"{name} = {NativeCompilerUtils.CSharpStringLiteral(value)}");

        return arguments.Count == 0
            ? $"[assembly: {type}]"
            : $"[assembly: {type}({string.Join(", ", arguments)})]";
    }

    // ---- Transitive project references (the well-known _MSBuildProjectReferenceExistent item) ----

    private static List<string> TransitiveProjectReferences(string csprojPath)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(csprojPath) };
        var pending = new Queue<string>();
        pending.Enqueue(Path.GetFullPath(csprojPath));

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var directory = Path.GetDirectoryName(current);
            if (directory is null) continue;

            XDocument document;
            try { document = XDocument.Load(current); }
            catch { continue; }

            foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                if (string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (((string?)reference.Attribute("OutputItemType") ?? "").Contains("Analyzer", StringComparison.OrdinalIgnoreCase))
                    continue;

                var include = (string?)reference.Attribute("Include");
                if (string.IsNullOrWhiteSpace(include)) continue;

                var resolved = ResolvePath(directory, include);
                if (resolved is null || !seen.Add(resolved)) continue;

                result.Add(resolved);
                pending.Enqueue(resolved);
            }
        }

        return result;
    }

    private static string? ResolvePath(string baseDirectory, string relative)
    {
        var normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
        return File.Exists(full) ? full : null;
    }

    private static List<(string? Type, List<string> Positional, Dictionary<string, string> Named)> ReadAttributeItems(
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> items)
    {
        var result = new List<(string?, List<string>, Dictionary<string, string>)>();
        if (!items.TryGetValue("AssemblyAttribute", out var attributes)) return result;

        foreach (var item in attributes)
        {
            var (positional, named) = SplitParameters(item);
            result.Add((item.GetValueOrDefault("Identity"), positional, named));
        }

        return result;
    }

    private static (List<string> Positional, Dictionary<string, string> Named) SplitParameters(Dictionary<string, string> item)
    {
        var positional = item
            .Where(pair => pair.Key.StartsWith("_Parameter", StringComparison.Ordinal)
                           && int.TryParse(pair.Key.AsSpan("_Parameter".Length), out _))
            .OrderBy(pair => int.Parse(pair.Key["_Parameter".Length..]))
            .Select(pair => pair.Value)
            .ToList();

        var named = item
            .Where(pair => !IsWellKnownMetadata(pair.Key) && !pair.Key.StartsWith("_Parameter", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return (positional, named);
    }

    // The SDK reads attribute arguments through `CloneCustomMetadata()`, which omits MSBuild's
    // well-known item metadata. The evaluator synthesizes that metadata for file items, so it must
    // be filtered out here or an attribute's type name would leak out as `FullPath = ...` etc.
    private static bool IsWellKnownMetadata(string name) =>
        name is "Identity" or "FullPath" or "RootDir" or "Filename" or "Extension" or "RelativeDir"
            or "Directory" or "RecursiveDir" or "ModifiedTime" or "CreatedTime" or "AccessedTime"
            or "DefiningProjectFullPath" or "DefiningProjectDirectory" or "DefiningProjectName"
            or "DefiningProjectExtension";

    // ---- Minimal MSBuild graph + target evaluation ------------------------------------------------

    private sealed class Graph
    {
        public string Directory = "";
        public readonly Dictionary<string, string> Props = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Dictionary<string, string>>> RootItems = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, XElement> Targets = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<(string Relative, string Full)>> _globCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(XElement Element, string Directory)> _pendingItems = new();
        private readonly HashSet<string> _visitedFiles = new(StringComparer.OrdinalIgnoreCase);

        public sealed class TargetResult
        {
            public List<(string? Type, List<string> Positional, Dictionary<string, string> Named)> Attributes { get; } = new();
            public Dictionary<string, List<Dictionary<string, string>>> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public static Graph Load(string csprojPath, Dictionary<string, Graph> cache)
        {
            var full = Path.GetFullPath(csprojPath);
            if (cache.TryGetValue(full, out var cached)) return cached;

            var graph = new Graph { Directory = Path.GetDirectoryName(full) ?? "" };
            cache[full] = graph;
            graph.SeedDefaults(full);

            var props = FindUpwards(full, "Directory.Build.props");
            if (props is not null) graph.LoadFile(props, cache);

            graph.LoadFile(full, cache);

            var targets = FindUpwards(full, "Directory.Build.targets");
            if (targets is not null) graph.LoadFile(targets, cache);

            if (!graph.Props.ContainsKey("AssemblyName"))
                graph.Props["AssemblyName"] = Path.GetFileNameWithoutExtension(full);

            graph.EvaluatePendingItems();

            return graph;
        }

        // The SDK props (Microsoft.Common.props / Microsoft.NET.Sdk.DefaultItems.props) are imported
        // by the real build but not by this evaluator, so a small set of defaults the imported
        // .props/.targets rely on (language extension, output roots, item excludes) is seeded here
        // before any file is loaded. Notably `$(DefaultLanguageSourceExtension)` would otherwise
        // expand to empty, turning the `**\*$(DefaultLanguageSourceExtension)` exclude into `**\*`
        // and dropping every asset.
        private void SeedDefaults(string csprojPath)
        {
            var directory = Path.GetDirectoryName(csprojPath) ?? "";
            var separator = Path.DirectorySeparatorChar.ToString();
            var root = Environment.GetEnvironmentVariable("NATIVECOMPILER_OUTPUT_ROOT") ?? "myc";
            var assemblyName = Path.GetFileNameWithoutExtension(csprojPath);

            void SetDefault(string key, string value)
            {
                if (!Props.ContainsKey(key)) Props[key] = value;
            }

            SetDefault("MSBuildProjectName", assemblyName);
            SetDefault("AssemblyName", assemblyName);
            SetDefault("RootNamespace", assemblyName);
            SetDefault("MSBuildProjectDirectory", directory);
            SetDefault("ProjectDir", directory + separator);
            SetDefault("DefaultLanguageSourceExtension", ".cs");
            SetDefault("AppDesignerFolder", "Properties");
            SetDefault("BaseIntermediateOutputPath", "obj" + separator);
            SetDefault("BaseOutputPath", root + separator);
            SetDefault("DefaultExcludesInProjectFolder", "");
            // Mirrors Microsoft.Common.props: the output roots plus project/solution/user files.
            Props["DefaultItemExcludes"] = root + "/**;bin/**;obj/**;**/*.user;**/*.*proj;**/*.sln;**/*.vssscc;**/.DS_Store";
        }

        private static string? FindUpwards(string startFile, string fileName)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(startFile)!);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            return null;
        }

        private void LoadFile(string path, Dictionary<string, Graph> cache)
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full) ?? "";
            if (!_visitedFiles.Add(full)) return;

            XDocument document;
            try { document = XDocument.Load(full); }
            catch { return; }

            if (document.Root is null) return;
            Props["MSBuildThisFileDirectory"] = directory + Path.DirectorySeparatorChar;

            foreach (var element in document.Root.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "Import":
                        var import = Expand((string?)element.Attribute("Project") ?? "", Props);
                        if (import.Length > 0)
                        {
                            var resolved = ResolvePath(directory, import);
                            if (resolved is not null) LoadFile(resolved, cache);
                        }
                        break;
                    case "PropertyGroup":
                        foreach (var property in element.Elements())
                            Props[property.Name.LocalName] = Expand(property.Value, Props).Trim();
                        break;
                    case "ItemGroup":
                        // MSBuild evaluates every property in all imports before any item, so item
                        // declarations are recorded here and expanded once all files are loaded.
                        foreach (var item in element.Elements())
                            _pendingItems.Add((item, Directory));
                        break;
                    case "Target":
                        var name = (string?)element.Attribute("Name");
                        if (!string.IsNullOrEmpty(name)) Targets[name] = element;
                        break;
                }
            }
        }

        private void EvaluatePendingItems()
        {
            foreach (var (element, directory) in _pendingItems)
                AddItem(RootItems, element, Props, directory);
        }

        public TargetResult EvaluateTarget(
            XElement target, List<string> projectReferences, Dictionary<string, Graph> cache)
        {
            var items = Clone(RootItems);
            var props = new Dictionary<string, string>(Props, StringComparer.OrdinalIgnoreCase);
            items["_MSBuildProjectReferenceExistent"] = projectReferences
                .Select(reference => NewItem(reference)).ToList();

            var result = new TargetResult { Items = items };

            foreach (var child in target.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "PropertyGroup":
                        foreach (var property in child.Elements())
                            props[property.Name.LocalName] = ExpandAll(property.Value, props, items).Trim();
                        break;

                    case "ItemGroup":
                        foreach (var item in child.Elements())
                        {
                            if (item.Name.LocalName == "AssemblyAttribute")
                            {
                                result.Attributes.AddRange(ExpandAttribute(item, props, items));
                            }
                            else
                            {
                                AddItem(items, item, props, Directory);
                            }
                        }
                        break;

                    case "MSBuild":
                        RunMsBuildTask(child, props, items, Directory, cache);
                        break;

                    case "RemoveDuplicates":
                        var removeDuplicates = RunRemoveDuplicates(child, props, items);
                        if (removeDuplicates is { } duplicateResult)
                            items[duplicateResult.ItemName] = duplicateResult.Filtered;
                        break;
                }
            }

            return result;
        }

        // <RemoveDuplicates Inputs="@(X)"><Output TaskParameter="Filtered" ItemName="Y"/></RemoveDuplicates>
        // Copies X into Y (metadata preserved), dropping later items with a duplicate identity.
        // Returns the target item name plus its filtered list; null when the task does not apply
        // (the caller decides where the result lands, keeping this helper free of mutation).
        [Pure]
        private static (string ItemName, List<Dictionary<string, string>> Filtered)? RunRemoveDuplicates(
            XElement task, Dictionary<string, string> props,
            Dictionary<string, List<Dictionary<string, string>>> items)
        {
            if (!EvaluateCondition((string?)task.Attribute("Condition"), props, items)) return null;
            if (!IsSingleItemRef((string?)task.Attribute("Inputs") ?? "", out var inputName)) return null;
            if (!items.TryGetValue(inputName, out var source)) return null;

            var output = task.Elements().FirstOrDefault(element => element.Name.LocalName == "Output");
            var itemName = (string?)output?.Attribute("ItemName");
            if (string.IsNullOrEmpty(itemName)) return null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filtered = new List<Dictionary<string, string>>();
            foreach (var item in source)
                if (seen.Add(item["Identity"]))
                    filtered.Add(new Dictionary<string, string>(item, StringComparer.OrdinalIgnoreCase));

            return (itemName, filtered);
        }

        private void RunMsBuildTask(XElement task, Dictionary<string, string> props,
            Dictionary<string, List<Dictionary<string, string>>> items, string projectDirectory,
            Dictionary<string, Graph> cache)
        {
            if (!EvaluateCondition((string?)task.Attribute("Condition"), props, items)) return;

            var output = task.Elements().FirstOrDefault(element => element.Name.LocalName == "Output");
            if (output is null) return;
            if (!string.Equals((string?)output.Attribute("TaskParameter"), "TargetOutputs", StringComparison.OrdinalIgnoreCase))
                return;

            var itemName = (string?)output.Attribute("ItemName");
            if (string.IsNullOrEmpty(itemName)) return;

            var targetNames = Expand((string?)task.Attribute("Targets") ?? "", props)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var projects = ExpandAll((string?)task.Attribute("Projects") ?? "", props, items)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var identities = new List<string>();
            foreach (var project in projects)
            {
                var resolved = Path.IsPathRooted(project)
                    ? project
                    : ResolvePath(projectDirectory, project);
                if (resolved is null) continue;

                var referenced = Load(resolved, cache);
                foreach (var targetName in targetNames)
                    identities.AddRange(referenced.EvaluateReturns(targetName));
            }

            items[itemName] = identities.Select(NewItem).ToList();
        }

        private List<string> EvaluateReturns(string targetName)
        {
            if (!Targets.TryGetValue(targetName, out var target)) return new();

            var items = Clone(RootItems);
            var props = new Dictionary<string, string>(Props, StringComparer.OrdinalIgnoreCase);

            foreach (var child in target.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "PropertyGroup":
                        foreach (var property in child.Elements())
                            props[property.Name.LocalName] = ExpandAll(property.Value, props, items).Trim();
                        break;
                    case "ItemGroup":
                        foreach (var item in child.Elements())
                            AddItem(items, item, props, Directory);
                        break;
                }
            }

            var identities = new List<string>();
            foreach (var itemName in ItemRefs((string?)target.Attribute("Returns") ?? ""))
                if (items.TryGetValue(itemName, out var list))
                    identities.AddRange(list.Select(item => item["Identity"]));

            return identities;
        }

        private List<(string? Type, List<string> Positional, Dictionary<string, string> Named)> ExpandAttribute(
            XElement element, Dictionary<string, string> props,
            Dictionary<string, List<Dictionary<string, string>>> items)
        {
            var results = new List<(string?, List<string>, Dictionary<string, string>)>();
            if (!EvaluateCondition((string?)element.Attribute("Condition"), props, items)) return results;

            var type = ExpandAll((string?)element.Attribute("Include") ?? "", props, items);
            var parameters = element.Elements().ToDictionary(
                parameter => parameter.Name.LocalName,
                parameter => parameter.Value);

            var batch = MetadataRef(parameters.Values);
            if (batch is null)
            {
                var (positional, named) = ExpandParameters(parameters, props, items, null, null);
                results.Add((type, positional, named));
                return results;
            }

            if (!items.TryGetValue(batch.Value.ItemName, out var batchItems) || batchItems.Count == 0)
                return results;

            foreach (var item in batchItems)
            {
                var (positional, named) = ExpandParameters(parameters, props, items, batch.Value.ItemName, item);
                results.Add((type, positional, named));
            }

            return results;
        }

        private (List<string>, Dictionary<string, string>) ExpandParameters(
            Dictionary<string, string> parameters, Dictionary<string, string> props,
            Dictionary<string, List<Dictionary<string, string>>> items, string? batchName,
            Dictionary<string, string>? batchItem)
        {
            var positional = parameters.Keys
                .Where(key => key.StartsWith("_Parameter", StringComparison.Ordinal) && int.TryParse(key.AsSpan("_Parameter".Length), out _))
                .OrderBy(key => int.Parse(key["_Parameter".Length..]))
                .ToList();

            var orderedPositional = positional
                .Select(key => ExpandValue(parameters[key], props, items, batchName, batchItem))
                .ToList();

            var named = parameters
                .Where(pair => !pair.Key.StartsWith("_Parameter", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => ExpandValue(pair.Value, props, items, batchName, batchItem));

            return (orderedPositional, named);
        }

        private static string ExpandValue(string value, Dictionary<string, string> props,
            Dictionary<string, List<Dictionary<string, string>>> items, string? batchName,
            Dictionary<string, string>? batchItem)
        {
            if (batchItem is not null)
            {
                if (batchName is not null)
                    value = MetadataToken.Replace(value, match =>
                        string.Equals(match.Groups["n"].Value, batchName, StringComparison.OrdinalIgnoreCase)
                            ? (batchItem.TryGetValue(match.Groups["m"].Value, out var metadata) ? metadata : "")
                            : match.Value);

                // An unqualified `%(Meta)` refers to the current batch item's own metadata, e.g.
                // `%(RelativeDir)%(Filename)%(Extension)` inside the module-asset LogicalName update.
                value = UnqualifiedMetadataToken.Replace(value, match =>
                    batchItem.TryGetValue(match.Groups[1].Value, out var metadata) ? metadata : "");
            }

            return ExpandAll(value, props, items);
        }

        private void AddItem(Dictionary<string, List<Dictionary<string, string>>> target,
            XElement element, Dictionary<string, string> props, string directory)
        {
            var name = element.Name.LocalName;

            var remove = (string?)element.Attribute("Remove");
            if (remove is not null)
            {
                if (target.TryGetValue(name, out var existing))
                {
                    var removeSet = new HashSet<string>(
                        ExpandAll(remove, props, target).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        StringComparer.OrdinalIgnoreCase);
                    existing.RemoveAll(item => removeSet.Contains(item["Identity"]));
                }
                return;
            }

            var update = (string?)element.Attribute("Update");
            if (update is not null)
            {
                var updatedItems = ApplyUpdate(target, name, element, props, directory, update);
                if (updatedItems is not null) target[name] = updatedItems;
                return;
            }

            var includeRaw = (string?)element.Attribute("Include") ?? "";
            if (includeRaw.Length == 0) return;

            var conditionRaw = (string?)element.Attribute("Condition");
            var batchName = DetectBatchItemType(includeRaw, conditionRaw);

            // Metadata batching: `%(Item.Meta)` (or a bare `%(Meta)` batch) inside the include or
            // condition creates one item per source item, e.g. the module-asset target's
            // `ModuleAssets Include="Areas\$(AssemblyName)\%(ModuleAssetFiles.RelativeDir)..."`.
            if (batchName is not null && target.TryGetValue(batchName, out var batchItems) && batchItems.Count > 0)
            {
                foreach (var batchItem in batchItems)
                {
                    var condition = ExpandValue(conditionRaw ?? "", props, target, batchName, batchItem);
                    if (!EvaluateCondition(condition, props, target)) continue;
                    var include = ExpandValue(includeRaw, props, target, batchName, batchItem);
                    var metadata = element.Elements().ToDictionary(
                        child => child.Name.LocalName,
                        child => ExpandValue(child.Value, props, target, batchName, batchItem));
                    AddIncludedItems(target, name, include, metadata, props, directory, exclude: "");
                }
                return;
            }

            if (!EvaluateCondition(conditionRaw, props, target)) return;

            // `Include="@(Other)"` copies those items verbatim, metadata included.
            if (IsSingleItemRef(includeRaw, out var referenced))
            {
                if (!target.TryGetValue(referenced, out var source)) return;
                var metadata = element.Elements().ToDictionary(
                    child => child.Name.LocalName,
                    child => ExpandAll(child.Value, props, target));
                foreach (var item in source)
                {
                    var clone = new Dictionary<string, string>(item, StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in metadata) clone[pair.Key] = pair.Value;
                    GetList(target, name).Add(clone);
                }
                return;
            }

            var excludeRaw = (string?)element.Attribute("Exclude") ?? "";
            AddIncludedItems(
                target, name,
                ExpandAll(includeRaw, props, target),
                element.Elements().ToDictionary(child => child.Name.LocalName, child => ExpandAll(child.Value, props, target)),
                props, directory,
                ExpandAll(excludeRaw, props, target));
        }

        // Applies <Item Update="..."> metadata edits. Returns the new item list (null when the
        // named item group is absent) so the caller decides where it lands. Metadata is expanded
        // against a clone that grows key-by-key, preserving the order an in-place edit had (a value
        // referencing an earlier key sees that key's new value).
        [Pure]
        private static List<Dictionary<string, string>>? ApplyUpdate(Dictionary<string, List<Dictionary<string, string>>> target,
            string name, XElement element, Dictionary<string, string> props, string directory, string updateRaw)
        {
            var conditionRaw = (string?)element.Attribute("Condition");
            var batchName = DetectBatchItemType(updateRaw, conditionRaw);

            if (!target.TryGetValue(name, out var items)) return null;

            var metadata = element.Elements().Select(child => (child.Name.LocalName, child.Value)).ToList();
            var effectiveBatch = batchName ?? name;

            var updated = new List<Dictionary<string, string>>(items.Count);
            foreach (var item in items)
            {
                var condition = ExpandValue(conditionRaw ?? "", props, target, effectiveBatch, item);
                if (!EvaluateCondition(condition, props, target))
                {
                    updated.Add(item);
                    continue;
                }

                var clone = new Dictionary<string, string>(item, StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in metadata)
                    clone[key] = ExpandValue(value, props, target, effectiveBatch, clone);
                updated.Add(clone);
            }

            return updated;
        }

        private void AddIncludedItems(Dictionary<string, List<Dictionary<string, string>>> target, string name,
            string include, Dictionary<string, string> metadata, Dictionary<string, string> props,
            string directory, string exclude)
        {
            if (include.Length == 0) return;

            var excludeSpecs = exclude.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var list = GetList(target, name);

            foreach (var spec in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (spec.IndexOfAny(GlobChars) >= 0)
                {
                    foreach (var (relative, full) in Glob(directory, spec, excludeSpecs))
                        list.Add(MergeMetadata(metadata, FileMetadata(directory, relative, full), relative));
                    continue;
                }

                list.Add(MergeMetadata(metadata, FileMetadata(directory, spec, null), spec));
            }
        }

        private static Dictionary<string, string> MergeMetadata(
            Dictionary<string, string> metadata, Dictionary<string, string> fileMetadata, string identity)
        {
            var item = new Dictionary<string, string>(fileMetadata, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in metadata) item[pair.Key] = pair.Value;
            item["Identity"] = identity;
            return item;
        }

        private static Dictionary<string, string> FileMetadata(string projectDirectory, string identity, string? absolutePath)
        {
            string full;
            try
            {
                full = absolutePath ?? (Path.IsPathRooted(identity)
                    ? identity
                    : Path.GetFullPath(Path.Combine(projectDirectory, identity.Replace('\\', Path.DirectorySeparatorChar))));
            }
            catch
            {
                full = identity;
            }

            string relative;
            try { relative = Path.GetRelativePath(projectDirectory, full).Replace('\\', '/'); }
            catch { relative = identity.Replace('\\', '/'); }

            var slash = relative.LastIndexOf('/');
            var relativeDir = slash < 0 ? "" : relative[..(slash + 1)];

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FullPath"] = full.Replace('\\', '/'),
                ["Directory"] = (Path.GetDirectoryName(full) ?? "").Replace('\\', '/'),
                ["Filename"] = Path.GetFileNameWithoutExtension(full),
                ["Extension"] = Path.GetExtension(full),
                ["RelativeDir"] = relativeDir,
            };
        }

        private static readonly char[] GlobChars = { '*', '?' };

        private List<(string Relative, string Full)> Glob(string projectDirectory, string pattern, string[] excludePatterns)
        {
            var cacheKey = pattern + "|" + string.Join("|", excludePatterns);
            if (_globCache.TryGetValue(cacheKey, out var cached)) return cached;

            var include = GlobToRegex(pattern.Replace('\\', '/'));
            var excludes = excludePatterns
                .Select(spec => GlobToRegex(spec.Replace('\\', '/')))
                .ToList();

            var matches = new List<(string Relative, string Full)>();
            if (System.IO.Directory.Exists(projectDirectory))
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
                    if (!include.IsMatch(relative)) continue;
                    if (excludes.Any(regex => regex.IsMatch(relative))) continue;
                    matches.Add((relative, file.Replace('\\', '/')));
                }
            }

            _globCache[cacheKey] = matches;
            return matches;
        }

        private static Regex GlobToRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var current = pattern[i];
                if (current == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                        {
                            i++;
                            builder.Append("(?:.*/)?");
                        }
                        else
                        {
                            builder.Append(".*");
                        }
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }
                }
                else if (current == '?')
                {
                    builder.Append("[^/]");
                }
                else if ("+.()|{}[]^$\\".IndexOf(current) >= 0)
                {
                    builder.Append('\\').Append(current);
                }
                else
                {
                    builder.Append(current);
                }
            }

            builder.Append('$');
            return new Regex(builder.ToString(), RegexOptions.CultureInvariant);
        }

        private static List<Dictionary<string, string>> GetList(
            Dictionary<string, List<Dictionary<string, string>>> target, string name)
        {
            if (!target.TryGetValue(name, out var list))
            {
                list = new List<Dictionary<string, string>>();
                target[name] = list;
            }
            return list;
        }

        private static string? DetectBatchItemType(params string?[] values)
        {
            string? found = null;
            foreach (var value in values)
            {
                if (string.IsNullOrEmpty(value)) continue;
                foreach (Match match in MetadataToken.Matches(value))
                {
                    var itemName = match.Groups["n"].Value;
                    if (found is null) found = itemName;
                    else if (!string.Equals(found, itemName, StringComparison.OrdinalIgnoreCase)) return null;
                }
            }
            return found;
        }

        private static bool IsSingleItemRef(string value, out string name)
        {
            name = "";
            var trimmed = value.Trim();
            var matches = ItemToken.Matches(trimmed);
            if (matches.Count != 1) return false;
            if (matches[0].Index != 0 || matches[0].Length != trimmed.Length) return false;
            name = matches[0].Groups[1].Value;
            return true;
        }
    }

    // ---- Expansion / conditions -------------------------------------------------------------------

    private const int MaxDepth = 16;

    private static readonly Regex PropertyToken = new(@"\$\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ItemToken = new(@"@\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MetadataToken = new(@"%\((?<n>[^).]+)\.(?<m>[^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnqualifiedMetadataToken = new(@"%\(([^).]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StringNewFunction = new(
        @"\$\(\[System\.String\]::new\('(?<s>[^']*)'\)(?<reps>(?:\.Replace\('(?<a>[^']*)',\s*'(?<b>[^']*)'\))*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReplaceCall = new(
        @"\.Replace\('(?<a>[^']*)',\s*'(?<b>[^']*)'\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Bounded support for the property functions the MSBuild targets in the wild use to compute item
    // metadata, e.g. OrchardCore's module-asset LogicalName:
    //   $([System.String]::new('$(AssemblyName).%(RelativeDir)%(FileName)%(Extension)')
    //       .Replace('\', '>').Replace('/', '>'))
    private static string EvaluatePropertyFunctions(string value)
    {
        if (value.IndexOf("$([System.String]::new(", StringComparison.Ordinal) < 0) return value;

        return StringNewFunction.Replace(value, match =>
        {
            var text = match.Groups["s"].Value;
            foreach (Match replace in ReplaceCall.Matches(match.Groups["reps"].Value))
                text = text.Replace(replace.Groups["a"].Value, replace.Groups["b"].Value);
            return text;
        });
    }


    private static string Expand(string value, IReadOnlyDictionary<string, string> props)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf("$(", StringComparison.Ordinal) < 0) return value ?? "";
        // MSBuild expands a property value once, at definition time, against the values already
        // resolved. Stored values are therefore already expanded, and a single pass is enough - a
        // loop would run away on a self-referential value such as
        // <DefaultItemExcludes>$(DefaultItemExcludes);x</DefaultItemExcludes>.
        return PropertyToken.Replace(value, match =>
            props.TryGetValue(match.Groups[1].Value, out var resolved) ? resolved : match.Value);
    }

    private static string ExpandAll(string value, IReadOnlyDictionary<string, string> props,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> items)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var current = value;
        for (var i = 0; i < MaxDepth; i++)
        {
            var next = ItemToken.Replace(current, match =>
                items.TryGetValue(match.Groups[1].Value, out var list)
                    ? string.Join(';', list.Select(item => item["Identity"]))
                    : "");
            next = Expand(next, props);
            next = EvaluatePropertyFunctions(next);
            if (next == current) break;
            current = next;
        }
        return current;
    }

    private static (string ItemName, string Metadata)? MetadataRef(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            var match = MetadataToken.Match(value ?? "");
            if (match.Success) return (match.Groups["n"].Value, match.Groups["m"].Value);
        }
        return null;
    }

    private static IEnumerable<string> ItemRefs(string value) =>
        ItemToken.Matches(value ?? "").Select(match => match.Groups[1].Value);

    private static bool EvaluateCondition(string? condition, IReadOnlyDictionary<string, string> props,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> items)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var value = StripBalancedParens(condition.Trim());

        if (value.StartsWith('!')) return !EvaluateCondition(value[1..], props, items);

        var orIndex = FindTopLevelOperator(value, "or");
        if (orIndex >= 0)
            return EvaluateCondition(value[..orIndex], props, items) || EvaluateCondition(value[(orIndex + 4)..], props, items);

        var andIndex = FindTopLevelOperator(value, "and");
        if (andIndex >= 0)
            return EvaluateCondition(value[..andIndex], props, items) && EvaluateCondition(value[(andIndex + 5)..], props, items);

        if (value.StartsWith("Exists(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            var inside = TrimQuotes(value["Exists(".Length..^1]);
            return File.Exists(ExpandAll(inside, props, items));
        }

        var comparison = Regex.Match(value, @"^(?<l>.*?)\s*(?<op>!=|==)\s*(?<r>.*?)$", RegexOptions.Singleline);
        if (!comparison.Success) return true;

        var left = Operand(comparison.Groups["l"].Value.Trim(), props, items);
        var right = Operand(comparison.Groups["r"].Value.Trim(), props, items);
        var equal = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        return comparison.Groups["op"].Value == "==" ? equal : !equal;
    }

    // Removes redundant outer parentheses, e.g. `(a == b)` -> `a == b`, so the operator split below
    // sees the real top-level structure: `!('a' == '' or 'b' == '.resx')` must negate the whole group.
    private static string StripBalancedParens(string value)
    {
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')')
        {
            var depth = 0;
            var wrapsWhole = true;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '(') depth++;
                else if (value[i] == ')')
                {
                    depth--;
                    if (depth == 0 && i != value.Length - 1) { wrapsWhole = false; break; }
                }
            }
            if (!wrapsWhole) break;
            value = value[1..^1].Trim();
        }
        return value;
    }

    // Finds a top-level ` and `/` or ` separator, ignoring occurrences nested in parentheses or quotes.
    private static int FindTopLevelOperator(string value, string op)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth--; continue; }
            if (depth != 0 || i + op.Length + 1 >= value.Length) continue;
            if (value[i] != ' ' || value[i + op.Length + 1] != ' ') continue;
            if (value.AsSpan(i + 1, op.Length).Equals(op, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string Operand(string token, IReadOnlyDictionary<string, string> props,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> items)
    {
        token = TrimQuotes(token);
        if (token.StartsWith("@(", StringComparison.Ordinal) && token.EndsWith(')'))
        {
            var name = token[2..^1];
            return items.TryGetValue(name, out var list) ? string.Join(';', list.Select(item => item["Identity"])) : "";
        }
        return Expand(token, props);
    }

    private static string TrimQuotes(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1];
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return value[1..^1];
        return value;
    }

    private static Dictionary<string, List<Dictionary<string, string>>> Clone(
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> source) =>
        source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(item => new Dictionary<string, string>(item, StringComparer.OrdinalIgnoreCase)).ToList(),
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> NewItem(string identity) =>
        new(StringComparer.OrdinalIgnoreCase) { ["Identity"] = identity };
}
