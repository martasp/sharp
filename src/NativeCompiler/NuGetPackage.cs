namespace NativeCompiler;

public static class NuGetPackage
{
    public static string FindPackageDLL(string dllPath)
    {
        var libPath = Path.Combine(dllPath, "lib", "netstandard2.0"); // not sure use netstandard2.0 or 6 now.

        if (Directory.Exists(libPath))
        {
            var dllFiles = Directory.GetFiles(libPath, "*.dll");

            if (dllFiles.Length > 0)
                // If you want to return the first found DLL, you can modify this line
                // to select a specific DLL based on your criteria.
                return dllFiles[0];
        }

        return string.Empty;
        //throw new Exception($"Cant find dll path {dllPath}"); // Package not found in NuGet package cache.
    }

    public static string FindPackagePath(string packageName, string packageVersion)
    {
        // Specify the path to the global package cache folder
        var globalPackagesFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        var packageDirectory = new DirectoryInfo(globalPackagesFolderPath);

        if (packageDirectory.Exists)
        {
            var packageDirectories = packageDirectory.GetDirectories($"{packageName}*");
            foreach (var packageDir in packageDirectories)
            {
                var versionDirectories = packageDir.GetDirectories();

                var matchingVersionDir = versionDirectories.FirstOrDefault(dir =>
                    dir.Name.Equals(packageVersion, StringComparison.OrdinalIgnoreCase));

                if (matchingVersionDir != null) return matchingVersionDir.FullName;
            }
        }

        return string.Empty;
        //throw new Exception($"Cant find package path: {packageName}"); // Package not found in NuGet package cache.
    }

    ////Todo not working
    //public static string FindPackagePath(string packageName, string packageVersion, string test)
    //{
    //    var nugetSettings = Settings.LoadDefaultSettings(root: null);
    //    var sourceRepositoryProvider =
    //        new SourceRepositoryProvider(new PackageSourceProvider(nugetSettings), Repository.Provider.GetCoreV3());

    //    var sourceRepositories = sourceRepositoryProvider.GetRepositories();

    //    foreach (var sourceRepository in sourceRepositories)
    //    {
    //        var resource = sourceRepository.GetResource<FindPackageByIdResource>();
    //        if (resource != null)
    //        {
    //            var packageInfo = resource.GetAllVersionsAsync(packageName, new NullSourceCacheContext(),
    //                    new NullLogger(), CancellationToken.None).Result
    //                .FirstOrDefault(package => package.OriginalVersion == packageVersion);

    //            if (packageInfo != null)
    //            {
    //                // Specify the path to the global package cache folder
    //                var globalPackagesFolderPath = Path.Combine(
    //                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    //                    ".nuget", "packages");

    //                var packagePath = Path.Combine(globalPackagesFolderPath, packageInfo.ToFullString(),
    //                    packageInfo.Version.ToString());

    //                if (Directory.Exists(packagePath)) return packagePath;
    //            }
    //        }
    //    }

    //    throw new Exception("Cant find package path"); // Package not found in NuGet package cache.
    //}
}