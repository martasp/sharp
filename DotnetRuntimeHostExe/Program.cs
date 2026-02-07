using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;



// --- 1. NATIVE HOSTFXR DEFINITIONS ---

// Struct for initialization parameters
[StructLayout(LayoutKind.Sequential)]
internal struct HostFxrInitializeParameters
{
    public nuint size;
    public IntPtr host_path;
    public IntPtr dotnet_root;
}

// P/Invoke declarations for hostfxr.dll functions
internal static partial class HostFxrNative
{
    private const string HostFxrDll = "hostfxr.dll"; // Change to "libhostfxr.so" for Linux/macOS

    [LibraryImport(HostFxrDll, EntryPoint = "hostfxr_initialize_for_runtime_config", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int hostfxr_initialize_for_runtime_config(
// The compiler requires this attribute to know how to pass the string.
// hostfxr on Windows expects UTF-16 (wide characters).
            string runtime_config_path,
      in HostFxrInitializeParameters parameters,
      out IntPtr host_context_handle);

    [LibraryImport(HostFxrDll, EntryPoint = "hostfxr_run_app")]
    internal static partial int hostfxr_run_app(IntPtr host_context_handle);

    [LibraryImport(HostFxrDll, EntryPoint = "hostfxr_close")]
    internal static partial int hostfxr_close(IntPtr host_context_handle);
}

// --- 2. HOSTING LOGIC ---

public class CustomDotNetHost
{
    // These constants must match your managed application's build output.
    // e.g., if you run 'dotnet build MyManagedApp.csproj', the output assembly 
    // and config file will reflect the project name (e.g., MyManagedApp).

    public static async Task<int> Main(string[] args)  // if running in normal mode runtime dontet the host is already initialized. You cant initailized second hostfxr.dll !!!!!!!!!!!!!!!!!!!!!
    {

        Debugger.Break();

        var runtimeConfigName = args.FirstOrDefault() ?? "MyApp.runtimeconfig.json";
        runtimeConfigName = Path.GetFullPath(runtimeConfigName);
        // print arg 
        Console.WriteLine($"Using runtime config: {runtimeConfigName}");

        IntPtr hostContext = IntPtr.Zero;
        int exitCode = -1;

        try
        {
            // 1. Initialize the HostFxr Context
            int hr = InitializeHost(out hostContext, runtimeConfigName);
            if (hr != 0)
            {
                Console.WriteLine($"Error: Failed to initialize host context (HRESULT: 0x{hr:X})");
                return hr;
            }

            // 2. Run the Managed Application's Main method
            Console.WriteLine("--- 2. Running Managed Application (hostfxr_run_app) ---");


            if (!File.Exists(runtimeConfigName))
            {
                Console.WriteLine($"ERROR: Runtime config file NOT FOUND at: {runtimeConfigName}");
                hostContext = IntPtr.Zero;
                return -1; // Or some other distinct error code
            }

            // This function takes control, loads hostpolicy, finds the entry assembly 
            // defined in the .runtimeconfig.json, and executes its Main method.
            exitCode = HostFxrNative.hostfxr_run_app(hostContext);

            Console.WriteLine($"Managed application exited with code: {exitCode}");
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Host Exception: {ex.Message}");
            return -1;
        }
        finally
        {
            // 3. Clean up
            if (hostContext != IntPtr.Zero)
            {
                HostFxrNative.hostfxr_close(hostContext);
                Console.WriteLine("Host context closed.");
                //Console.WriteLine("Press Enter to exit...");
                //Console.ReadLine();
            }
        }
    }

    private static int InitializeHost(out IntPtr hostContext, string RuntimeConfigName)
    {
        Console.WriteLine($"--- 1. Initializing HostFxr for: {RuntimeConfigName} ---");

        // Define initialization parameters
        HostFxrInitializeParameters initParams = new HostFxrInitializeParameters
        {
            size = (nuint)Marshal.SizeOf<HostFxrInitializeParameters>(),
            host_path = IntPtr.Zero,
            dotnet_root = IntPtr.Zero
        };

        // Initialize based on the app's runtime configuration file
        int hr = HostFxrNative.hostfxr_initialize_for_runtime_config(
          RuntimeConfigName,
          in initParams,
          out hostContext);

        Console.WriteLine(hr == 0 ? "Initialization SUCCESS." : "Initialization FAILED.");
        return hr;
    }
}


//https://github.com/dotnet/runtime/blob/main/docs/design/features/host-error-codes.md?plain=1