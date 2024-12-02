using FluentResults;
using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IWebauthnService
    {
        Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds);

        Task<Result<string>> RegisterAttestStoreAuthenticator();

        Task<Result<string>> AuthenticateCredential(string credentialIdBase64, string[] transports);
    }

    public record CredentialWithPRF(string CredentialID, string[] Transports);
}
