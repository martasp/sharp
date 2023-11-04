using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;
using static NativeCompiler.NativeCompilerUtils;

namespace NativeCompilerTests;

[UsesVerify]
public class Sample
{
    [Fact]
    public Task Verify_GetdefaultReferences()
    {
        return Verify(GetdefaultReferences("./"));
    }

    [Fact]
    public Task Verify_ReadAllProjectFilesPath()
    {
        return Verify(ReadAllProjectFilesPath("./"));
    }
}