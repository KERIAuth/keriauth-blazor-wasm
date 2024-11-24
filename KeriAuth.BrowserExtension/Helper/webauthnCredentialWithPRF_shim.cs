using FluentResults;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

        [JSImport("test", "webauthnCredentialWithPRF")]
        internal static partial Task<string> Test();

        [JSImport("createCred", "webauthnCredentialWithPRF")]
        internal static partial Task CreateCred();

        [JSImport("createAndStoreCredential", "webauthnCredentialWithPRF")]
        internal static partial Task CreateAndStoreCredential();

        [JSImport("registerCredential", "webauthnCredentialWithPRF")]
        internal static partial Task RegisterCredential();

        [JSImport("authenticateCredential", "webauthnCredentialWithPRF")]
        internal static partial Task AuthenticateCredential();
    }
}