using FluentResults;
using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IWebauthnService
    {
        Task<Result<CredentialWithPRF>> RegisterCredentialAsync();

        Task<Result<string>> RegisterAttestStoreAuthenticator(string passcode);

        Task<Result<string>> AuthenticateCredential(string credentialIdBase64, string[] transports);
    }
}
