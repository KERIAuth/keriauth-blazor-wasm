using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace KeriAuth.BrowserExtension.Helper
{
    // Important: keep the imported method and property names aligned with the ts (or js) file

    [SupportedOSPlatform("browser")]
    public partial class KaleWebauthn
    {
        [JSImport("register", "kaleWebauthn")]
        internal static partial Task Register();
    }
}
