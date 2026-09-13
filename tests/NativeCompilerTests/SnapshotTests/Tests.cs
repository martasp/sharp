using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;
using static NativeCompiler.NativeCompilerUtils;

namespace NativeCompilerTests.SnapshotTests;

public class Sample
{
    [Fact]
    public Task Verify_GetdefaultReferences()
    {
        return Verify(GetdefaultReferencesForCompiling("./"));
    }

    [Fact]
    public Task Verify_ReadAllProjectFilesPath()
    {
        // Filter transient test-runner artifacts (MTP diagnostic logs, Verify .received.txt files)
        // out of the snapshot rather than deleting them — the log directory is locked by the
        // diagnostic logger process during the test run.
        var files = ReadAllProjectFilesPath("./")
            .Where(path =>
                !path.Contains(Path.DirectorySeparatorChar + "Log" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "Log" + Path.AltDirectorySeparatorChar)
                && !path.EndsWith(".diag", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Contains(".received.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Verify(files);
    }
}