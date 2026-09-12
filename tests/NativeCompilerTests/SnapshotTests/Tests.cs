using System.IO;
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
        // Test-runner artifacts are transient (MTP diagnostic logs get unique names per run,
        // stale .received.txt files linger after a previous failure) - clear them so the
        // snapshot only reflects real project output.
        var logDir = Path.Combine(Directory.GetCurrentDirectory(), "Log");
        if (Directory.Exists(logDir))
            Directory.Delete(logDir, true);
        foreach (var staleReceived in Directory.GetFiles(Directory.GetCurrentDirectory(), "Sample.*.received.txt"))
            File.Delete(staleReceived);

        return Verify(ReadAllProjectFilesPath("./"));
    }
}