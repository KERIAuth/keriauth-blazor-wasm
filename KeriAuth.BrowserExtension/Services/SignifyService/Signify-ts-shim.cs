using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace KeriAuth.BrowserExtension.Services.SignifyService
{
    // keep the imported method and property names aligned with signify_ts_shim.ts
    [SupportedOSPlatform("browser")]
    public partial class Signify_ts_shim
    {
        [JSImport("bootAndConnect", "signify_ts_shim")]
        internal static partial Task<string> BootAndConnect(string agentUrl, string bootUrl, string passcode);

        [JSImport("connect", "signify_ts_shim")]
        internal static partial Task<string> Connect(string agentUrl, string passcode);

        [JSImport("createAID", "signify_ts_shim")]
        internal static partial Task<string> CreateAID(string name);

        [JSImport("getAIDs", "signify_ts_shim")]
        internal static partial Task<string> GetAIDs();

        [JSImport("getAID", "signify_ts_shim")]
        internal static partial Task<string> GetAID(string prefix);
    }
}