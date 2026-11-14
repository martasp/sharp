using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotnetRuntime11
{
    public class DotnetRuntime11
    {
        [UnmanagedCallersOnly(EntryPoint = "Run", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int Run()
        {
            Console.WriteLine("Hello from DotnetRuntime11!");

            // Write some code that is not working in .net native aot like reflection havey stuff
            


            return 42;
        }

        [UnmanagedCallersOnly(EntryPoint = "Run1", CallConvs = new[] { typeof(CallConvCdecl) })]
        public static int RunAot()
        {
            Console.WriteLine("Hello from DotnetRuntime11!");
            return 42;
        }
    }
}
