using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace KeriAuth.BrowserExtension.Helper
{
    // Important: keep the imported method and property names aligned with the ts file
    [SupportedOSPlatform("browser")]
    public partial class WebauthnCredentialWithPRF
    {
        [JSImport("checkWebAuthnSupport", "webauthnCredentialWithPRF")]
        internal static partial bool CheckWebAuthnSupport();

        [JSImport("getProfileIdentifier", "webauthnCredentialWithPRF")]
        internal static partial Task<string> GetProfileIdentifier();

        [JSImport("createCred", "webauthnCredentialWithPRF")]
        internal static partial Task CreateCred();

        [JSImport("registerCredential", "webauthnCredentialWithPRF")]
        internal static partial Task RegisterCredential();

        [JSImport("authenticateCredential", "webauthnCredentialWithPRF")]
        internal static partial Task AuthenticateCredential();
    }
}