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
                foreach (var (type, positional, named) in app.EvaluateTarget(target, projectReferences, cache))
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
            .Where(pair => pair.Key != "Identity" && !pair.Key.StartsWith("_Parameter", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return (positional, named);
    }

    // ---- Minimal MSBuild graph + target evaluation ------------------------------------------------

    private sealed class Graph
    {
        public string Directory = "";
        public readonly Dictionary<string, string> Props = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Dictionary<string, string>>> RootItems = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, XElement> Targets = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _visitedFiles = new(StringComparer.OrdinalIgnoreCase);

        public static Graph Load(string csprojPath, Dictionary<string, Graph> cache)
        {
            var full = Path.GetFullPath(csprojPath);
            if (cache.TryGetValue(full, out var cached)) return cached;

            var graph = new Graph { Directory = Path.GetDirectoryName(full) ?? "" };
            cache[full] = graph;

            var props = FindUpwards(full, "Directory.Build.props");
            if (props is not null) graph.LoadFile(props, cache);

            graph.LoadFile(full, cache);

            var targets = FindUpwards(full, "Directory.Build.targets");
            if (targets is not null) graph.LoadFile(targets, cache);

            if (!graph.Props.ContainsKey("AssemblyName"))
                graph.Props["AssemblyName"] = Path.GetFileNameWithoutExtension(full);

            return graph;
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
                        foreach (var item in element.Elements())
                            AddItem(RootItems, item, Props, directory);
                        break;
                    case "Target":
                        var name = (string?)element.Attribute("Name");
                        if (!string.IsNullOrEmpty(name)) Targets[name] = element;
                        break;
                }
            }
        }

        public List<(string? Type, List<string> Positional, Dictionary<string, string> Named)> EvaluateTarget(
            XElement target, List<string> projectReferences, Dictionary<string, Graph> cache)
        {
            var items = Clone(RootItems);
            var props = new Dictionary<string, string>(Props, StringComparer.OrdinalIgnoreCase);
            items["_MSBuildProjectReferenceExistent"] = projectReferences
                .Select(reference => NewItem(reference)).ToList();

            var results = new List<(string?, List<string>, Dictionary<string, string>)>();

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
                                results.AddRange(ExpandAttribute(item, props, items));
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
                }
            }

            return results;
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
            if (batchName is not null && batchItem is not null)
                value = MetadataToken.Replace(value, match =>
                    string.Equals(match.Groups["n"].Value, batchName, StringComparison.OrdinalIgnoreCase)
                        && batchItem.TryGetValue(match.Groups["m"].Value, out var metadata)
                        ? metadata
                        : match.Value);

            return ExpandAll(value, props, items);
        }

        private static void AddItem(Dictionary<string, List<Dictionary<string, string>>> target,
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

            var include = ExpandAll((string?)element.Attribute("Include") ?? "", props, target);
            if (include.Length == 0) return;
            if (!EvaluateCondition((string?)element.Attribute("Condition"), props, target)) return;

            var metadata = element.Elements().ToDictionary(
                child => child.Name.LocalName,
                child => ExpandAll(child.Value, props, target));

            if (!target.TryGetValue(name, out var list))
            {
                list = new List<Dictionary<string, string>>();
                target[name] = list;
            }

            foreach (var identity in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var item = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["Identity"] = identity,
                };
                list.Add(item);
            }
        }
    }

    // ---- Expansion / conditions -------------------------------------------------------------------

    private const int MaxDepth = 16;

    private static readonly Regex PropertyToken = new(@"\$\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ItemToken = new(@"@\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MetadataToken = new(@"%\((?<n>[^).]+)\.(?<m>[^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
        var value = condition.Trim();

        var orIndex = value.IndexOf(" or ", StringComparison.OrdinalIgnoreCase);
        if (orIndex >= 0)
            return EvaluateCondition(value[..orIndex], props, items) || EvaluateCondition(value[(orIndex + 4)..], props, items);

        var andIndex = value.IndexOf(" and ", StringComparison.OrdinalIgnoreCase);
        if (andIndex >= 0)
            return EvaluateCondition(value[..andIndex], props, items) && EvaluateCondition(value[(andIndex + 5)..], props, items);

        if (value.StartsWith('!')) return !EvaluateCondition(value[1..], props, items);

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
