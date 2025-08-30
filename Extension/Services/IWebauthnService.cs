using FluentResults;
using Extension.Models;

namespace Extension.Services
{
    public interface IWebauthnService
    {
        Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds, string residentKey, string authenticatorAttachment, string userVerification, string attestationConveyancePreference, List<string> hints);

        Task<Result<string>> RegisterAttestStoreAuthenticator(string residentKey, string authenticatorAttachment, string userVerification, string attestationConveyancePreference, List<string> hints);

        Task<Result<AuthenticateCredResult>> AuthenticateCredential(List<string> credentialIdBase64);

        Task<Result<string>> AuthenticateAKnownCredential();

        Task<RegisteredAuthenticators> GetRegisteredAuthenticators();

    }

    // shape and names need to align with that in webauthn....ts
    public record CredentialWithPRF(string CredentialId, string[] Transports);

    // shape and names need to align with that in webauthn....ts
    public record AuthenticateCredResult(string CredentialId, string EncryptKey);
}
