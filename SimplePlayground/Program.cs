using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;

var syntaxTree = CSharpSyntaxTree.ParseText(@"
using System;
public class C
{
    public static void Main()
    {
        System.Console.WriteLine(""Hello World!"");
        System.Console.ReadLine();
    }   
}");

var references = new[]
{
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.dll"),

MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.InteropServices.RuntimeInformation.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\mscorlib.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Web.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Data.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\netstandard.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\WindowsBase.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Core.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Linq.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Memory.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Drawing.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Buffers.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Console.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Windows.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Pipes.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Http.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Mail.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Ping.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Quic.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Numerics.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.Linq.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\Microsoft.CSharp.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Text.Json.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.XPath.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.AppContext.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ValueTuple.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Data.Common.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Collections.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Formats.Tar.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Sockets.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ObjectModel.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Formats.Asn1.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Pipelines.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Requests.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Security.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.WebProxy.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Transactions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Configuration.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Globalization.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.FileSystem.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Linq.Parallel.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Http.Json.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.WebClient.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Text.Encoding.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.XDocument.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\Microsoft.VisualBasic.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ComponentModel.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Compression.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Linq.Queryable.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.WebSockets.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Loader.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ServiceProcess.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Dynamic.Runtime.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.Emit.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Handles.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Claims.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Tasks.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Timer.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Web.HttpUtility.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.XmlDocument.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Linq.Expressions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.HttpListener.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.ServicePoint.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Numerics.Vectors.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Resources.Reader.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Resources.Writer.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Numerics.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ServiceModel.Web.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Thread.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.ReaderWriter.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.Debug.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.Tools.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\Microsoft.Win32.Registry.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.Serialization.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.XmlSerializer.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Drawing.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.IsolatedStorage.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.NameResolution.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Extensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Intrinsics.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Principal.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Text.Encodings.Web.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Channels.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Transactions.Local.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.Process.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.Tracing.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\Microsoft.VisualBasic.Core.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\Microsoft.Win32.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.Metadata.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Xml.XPath.XDocument.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.MemoryMappedFiles.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Linq.AsyncEnumerable.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.ServerSentEvents.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Overlapped.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.ThreadPool.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.Contracts.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Collections.Immutable.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Compression.Brotli.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.FileSystem.Watcher.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.WebSockets.Client.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.Extensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Serialization.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.SecureString.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Data.DataSetExtensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.StackTrace.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Collections.Concurrent.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Collections.NonGeneric.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Compression.ZipFile.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Pipes.AccessControl.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.NetworkInformation.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.AccessControl.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.TraceSource.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.FileSystem.DriveInfo.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Collections.Specialized.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Globalization.Calendars.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Net.WebHeaderCollection.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.InteropServices.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Text.Encoding.CodePages.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Text.RegularExpressions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.AccessControl.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.DispatchProxy.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Tasks.Dataflow.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Globalization.Extensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.FileSystem.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.UnmanagedMemoryStream.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Text.Encoding.Extensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Tasks.Parallel.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ComponentModel.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.Compression.FileSystem.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.TypeExtensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Resources.ResourceManager.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Serialization.Xml.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.Cng.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.Csp.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ComponentModel.Annotations.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Serialization.Json.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Principal.Windows.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Threading.Tasks.Extensions.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.FileVersionInfo.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.IO.FileSystem.AccessControl.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.Emit.Lightweight.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.DiagnosticSource.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ComponentModel.TypeConverter.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Reflection.Emit.ILGeneration.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.OpenSsl.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ComponentModel.DataAnnotations.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.ComponentModel.EventBasedAsync.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.Encoding.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.CompilerServices.Unsafe.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.CompilerServices.VisualC.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Serialization.Formatters.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.Serialization.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.Algorithms.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.Primitives.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Runtime.InteropServices.JavaScript.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Diagnostics.TextWriterTraceListener.dll"),
MetadataReference.CreateFromFile(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.0\ref\net10.0\System.Security.Cryptography.X509Certificates.dll"),

    // Add other necessary reference assemblies as needed
};

//var  mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
//references = [mscorlib];

var optionsExe = new CSharpCompilationOptions(OutputKind.ConsoleApplication);
optionsExe.WithPlatform(Platform.X64);
optionsExe.WithOptimizationLevel(OptimizationLevel.Debug);

var optionsDll = new CSharpCompilationOptions(OutputKind.ConsoleApplication);
optionsDll.WithPlatform(Platform.AnyCpu);
optionsDll.WithOptimizationLevel(OptimizationLevel.Debug);

var compilationDll = CSharpCompilation.Create(
    assemblyName: "MyApp",
    syntaxTrees: new[] { syntaxTree },
    references: references,
    options: optionsDll
);

var emitResultDll = compilationDll.Emit("MyApp.dll", "MyApp.pdb");
var path = Path.GetFullPath("MyApp.dll");

references = references.Append(MetadataReference.CreateFromFile(path)).ToArray();

var compilationExe = CSharpCompilation.Create(
    assemblyName: "MyApp",
    syntaxTrees: new[] { syntaxTree },
    references: references,
    options: optionsExe
);

//using var ms = new MemoryStream();
//var emitResult = compilation.Emit(ms, "output.pdb");
//var emitResult = compilation.Emit(ms, "output.pdb");
var emitResult = compilationExe.Emit("MyApp.exe", "MyApp.pdb");
if (!emitResult.Success)
{
    foreach (var diagnostic in emitResult.Diagnostics)
        Console.WriteLine(diagnostic.ToString());
}
else
{
    Console.WriteLine("Compilation succeeded.");
    // writing app to path:
    Console.WriteLine(Path.GetFullPath("MyApp.exe"));
    //File.WriteAllBytes("MyApp.exe", ms.ToArray());

    // Running app
    Console.WriteLine("Running app "+ Path.GetFullPath("MyApp.exe"));

    //run executable
    var process = System.Diagnostics.Process.Start("MyApp.exe");
    process.WaitForExit();

    Console.ReadKey();

}
