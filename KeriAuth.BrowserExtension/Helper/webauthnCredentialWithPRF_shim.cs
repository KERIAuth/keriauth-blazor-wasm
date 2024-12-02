using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using FluentResults;
using System.Threading.Tasks;
using System.Text.Json;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection.Metadata.Ecma335;


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

        [JSImport("authenticateCredential", "webauthnCredentialWithPRF")]
        internal static partial Task<string> AuthenticateCredential();
    }

    // Intermediate class to match the JSON structure
    class IntermediateResult
    {
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    public record CredentialWithPRF(string CredentialID, string[] Transports);

    public record CredentialWithPRFAndEncryptKey(string CredentialID, string[] Transports, string EncryptKey);


    public class CredentialWithPRF2
    {
        public string CredentialID { get; set; } // Corresponds to `credentialID` in TypeScript
        public string[] Transports { get; set; } // Corresponds to `transports` in TypeScript
    }




}

