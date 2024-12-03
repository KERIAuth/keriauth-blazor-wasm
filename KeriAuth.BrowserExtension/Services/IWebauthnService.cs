using FluentResults;
using KeriAuth.BrowserExtension.Models;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IWebauthnService
    {
        Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds);

        Task<Result<string>> RegisterAttestStoreAuthenticator();

        Task<Result<AuthenticateCredResult>> AuthenticateCredential(List<string> credentialIdBase64);

        Task<Result<string>> AuthenticateAKnownCredential();

        Task<RegisteredAuthenticators> getRegisteredAuthenticators();

    }

    // shape and names need to align with that in webauthn....ts
    public record CredentialWithPRF(string CredentialId, string[] Transports);

    // shape and names need to align with that in webauthn....ts
    public record AuthenticateCredResult(string CredentialId, string EncryptKey);
}
