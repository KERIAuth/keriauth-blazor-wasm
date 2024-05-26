using KeriAuth.BrowserExtension.Services;
using Microsoft.JSInterop;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeriAuth.BrowserExtension.Helper;

//    // keep the imported method and property names aligned with storageHelper.ts
//    [SupportedOSPlatform("browser")]
//    public partial class StorageHelper
//    {
//        private readonly IJSRuntime _jsRuntime;

//        public StorageHelper(IJSRuntime jsRuntime)
//        {
//            _jsRuntime = jsRuntime;
//        }   

//        public async Task AddStorageChangeListenerPublic(DotNetObjectReference<StorageChangeHandler> dotNetObjectReference)
//        {
//            await AddStorageChangeListener(dotNetObjectReference);
//        }

//        [JSImport("addStorageChangeListener", "storageHelper")]
//        internal static partial Task AddStorageChangeListener([JsMarshalAsObject] DotNetObjectReference<StorageChangeHandler> dotNetObjectReference);
//    }
//}

public static class StorageHelper
{
    // Helper function to convert a JsonDocument to a string
    public static string ToJsonString(this JsonDocument jdoc, bool Indented = false)
    {
        using var stream = new MemoryStream();
        // TODO P3 Consider adding an Encoder to JsonWriterOptions, in order to avoid extra escaping. See
        // https://learn.microsoft.com/en-US/dotnet/api/system.text.json.jsonwriteroptions.encoder?view=net-7.0
        Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = Indented });
        jdoc.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}