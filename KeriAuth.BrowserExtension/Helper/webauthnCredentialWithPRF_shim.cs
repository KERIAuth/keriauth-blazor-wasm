using FluentResults;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace KeriAuth.BrowserExtension.Helper
{
    // Important: keep the imported method and property names aligned with the ts file
    [SupportedOSPlatform("browser")]
    public partial class WebauthnCredentialWithPRF
    {
        // TODO fix return type. handle the ts Result<Foo> types
        [JSImport("checkWebAuthnSupport", "webauthnCredentialWithPRF")]
        internal static partial void CheckWebAuthnSupport();
    }
}