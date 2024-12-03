using FluentResults;
using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IWebauthnService
    {
        Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds);

        Task<Result<string>> RegisterAttestStoreAuthenticator();

        Task<Result<string>> AuthenticateCredential(List<string> credentialIdBase64);
    }

    // shape and names need to align with 
    public record CredentialWithPRF(string CredentialId, string[] Transports);
}
