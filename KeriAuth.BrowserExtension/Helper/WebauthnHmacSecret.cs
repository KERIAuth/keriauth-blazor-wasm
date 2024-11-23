using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace KeriAuth.BrowserExtension.Helper
{
    // Important: keep the imported method and property names aligned with the ts (or js) file

    [SupportedOSPlatform("browser")]
    public partial class WebauthnHmacSecret
    {
        [JSImport("registerCredential", "webauthnHmacSecret")]
        internal static partial Task RegisterCredential();
    }
}
