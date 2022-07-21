using NativeCompiler;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

using static VerifyXunit.Verifier;
using static NativeCompiler.NativeCompilerUtils;

namespace NativeCompilerTests
{
    [UsesVerify]
    public class Sample
    {
        [Fact]
        public Task Verify_GetdefaultReferences() => Verify(NativeCompilerUtils.GetdefaultReferences("./"));

        [Fact]
        public Task Verify_ReadAllProjectFilesPath() => Verify(NativeCompilerUtils.ReadAllProjectFilesPath("./"));
    }
}
