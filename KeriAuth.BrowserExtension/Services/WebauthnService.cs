using FluentResults;
using JsBind.Net;
using KeriAuth.BrowserExtension.Models;
using Microsoft.JSInterop;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using WebExtensions.Net;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace KeriAuth.BrowserExtension.Services
{
    public class WebauthnService(IJSRuntime jsRuntime, IJsRuntimeAdapter jsRuntimeAdapter, ILogger<WebauthnService> logger) : IWebauthnService
    {
        private IJSObjectReference? interopModule;
        private WebExtensionsApi? webExtensionsApi;

        async Task Initialize()
        {
            // TODO P2 might be able to instead move some of this into program.cs and inject these as parameters
            try
            {
                webExtensionsApi ??= new WebExtensionsApi(jsRuntimeAdapter);
                interopModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/webauthnCredentialWithPRF.js");
            }
            catch (JSException jsEx)
            {
                logger.LogError("Could not initialize {e}", jsEx.Message);
                throw new Exception(jsEx.Message);
            }
            catch (Exception ex)
            {
                logger.LogError("Could not initialize {e}", ex.Message);
                throw;
            }
        }

        public async Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds)
        {
            try
            {
                // Attempt to call the JavaScript function and map to the CredentialWithPRF type
                await Initialize();
                var credential = await interopModule!.InvokeAsync<CredentialWithPRF>("registerCredential", registeredCredIds);
                // logger.LogWarning("credential: {c}", credential);
                return Result.Ok(credential);
            }
            catch (JSException jsEx)
            {
                // Return a failure with a meaningful message and error metadata
                return Result.Fail(new FluentResults.Error("JavaScript error occurred")
                    .CausedBy(jsEx)
                    .WithMetadata("Function", "registerCredential"));
            }
            catch (Exception ex)
            {
                // Return a failure for unexpected exceptions
                return Result.Fail(new FluentResults.Error("Unexpected error occurred")
                    .CausedBy(ex));
            }
        }

        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
            //Converters =
            //{
            //    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            //}
        };

        public async Task<Result<AuthenticateCredResult>> AuthenticateCredential(List<string> credentialIdBase64s)
        {
            // logger.LogWarning("credentialIdBase64s: {r}", credentialIdBase64s);
            try
            {
                // Attempt to authenticate the credential and re-compute the encryption key
                await Initialize();
                var authenticateCredResult = await interopModule!.InvokeAsync<AuthenticateCredResult>("authenticateCredential", credentialIdBase64s);

                if (authenticateCredResult is not null)
                {
                    return Result.Ok(authenticateCredResult);
                }
                else
                {
                    return Result.Fail("failed to authenticate credential");
                }
            }
            catch (JSException jsEx)
            {
                // Return a failure with a meaningful message and error metadata
                return Result.Fail(new FluentResults.Error("JavaScript error occurred")
                    .CausedBy(jsEx)
                    .WithMetadata("Function", "authenticateCredential"));
            }
            catch (Exception ex)
            {
                // Return a failure for unexpected exceptions
                return Result.Fail(new FluentResults.Error("Unexpected error occurred in AuthenticateCredential")
                    .CausedBy(ex));
            }
        }

        private string getEncryptKeyBase64(string encryptKey)
        {
            // _logger.LogWarning("encryptKey original length (chars): {b}", encryptKey.Length);

            // Step 1: Adjust the raw key to ensure 32 credentialIdBytes (256 bits)
            byte[] rawKeyBytes = Encoding.UTF8.GetBytes(encryptKey);

            // Truncate or pad the key to exactly 32 credentialIdBytes
            byte[] adjustedKeyBytes = new byte[32];
            Buffer.BlockCopy(rawKeyBytes, 0, adjustedKeyBytes, 0, Math.Min(rawKeyBytes.Length, 32));

            // Step 2: Convert the adjusted raw key to Base64
            string encryptKeyBase64 = Convert.ToBase64String(adjustedKeyBytes);
            // _logger.LogWarning("encryptKeyBase64 adjusted length (chars): {b}", encryptKeyBase64.Length);

            return encryptKeyBase64;
        }

        public async Task<Result<string>> RegisterAttestStoreAuthenticator()
        {
            await Initialize();
            // Get list of currently registered authenticators, so there isn't an attempt to create redundant credentials (i.e., same RP and user) on same authenticator
            var registeredAuthenticators = await getRegisteredAuthenticators();

            


            if (registeredAuthenticators is null)
            {
                throw new ArgumentNullException(nameof(registeredAuthenticators));
            }

            // populate a list of registered authenticator credential ids
            List<string> registeredCredIds = [];
            foreach (var authenticator in registeredAuthenticators.Authenticators)
            {
                registeredCredIds.Add(authenticator.CredentialBase64);
            }

            // Register a new authenticator, excluding reregistering any authenticator already having a credential with same RP and user
            var credentialRet = await RegisterCredentialAsync(registeredCredIds);
            if (credentialRet is null || credentialRet.IsFailed)
            {
                return Result.Fail("Failed to register authenticator 333");
            }

            // Now that it is registered, we need to confirm that with user, and get the encrypt key, derrived from the PRF attestation
            // First, some prep
            CredentialWithPRF credential = credentialRet.Value;
            string credentialIdBase64Url = credential.CredentialId;
            // logger.LogWarning("credentialIdBase64: {b}", credentialIdBase64Url);
            List<string> credentialIds = [];
            credentialIds.Add(credentialIdBase64Url);

            // Now, get the attestation from same authenticator
            var encryptKeyBase64Ret = await AuthenticateCredential(credentialIds);
            if (encryptKeyBase64Ret is null || encryptKeyBase64Ret.IsFailed)
            {
                return Result.Fail("Failed to verify with authenticator 444");
            }

            // Get the cleartext passcode from session storage
            var passcodeElement = await webExtensionsApi!.Storage.Session.Get("passcode");
            if (passcodeElement.TryGetProperty("passcode", out JsonElement passcodeElement2) && passcodeElement2.ValueKind == JsonValueKind.String)
            {
                try
                {
                    var passcode = passcodeElement2.GetString();
                    if (passcode is null)
                    {
                        return Result.Fail("no passcode is cached");
                    }

                    // Encrypt the passcode with the encryptKey generated with the help of PRF from the authenticator
                    // First, convert the encrypt key to Base64
                    var encryptKeyBase64 = getEncryptKeyBase64(encryptKeyBase64Ret.Value.EncryptKey);

                    // Convert the passcode to Base64
                    byte[] plainBytes = Encoding.UTF8.GetBytes(passcode);
                    string passcodeBase64 = Convert.ToBase64String(plainBytes);

                    // confirm the encryptKey size
                    var encryptKeyBytes = Convert.FromBase64String(encryptKeyBase64);
                    // Console.WriteLine($"Key length in bytes: {encryptKeyBytes.Length}");
                    if (encryptKeyBytes.Length != 16 && encryptKeyBytes.Length != 32)
                    {
                        throw new InvalidOperationException("Encryption key must be 16 or 32 bytes.");
                    }

                    // Encrypt
                    var encryptedPasscodeBase64 = await interopModule!.InvokeAsync<string>("encryptWithNounce", encryptKeyBase64, passcodeBase64);

                    // convert encrypted passcode to bytes
                    var encryptedBytes = Convert.FromBase64String(encryptedPasscodeBase64);

                    // Verify the expected length
                    // Console.WriteLine($"Encrypted data length: {encryptedBytes.Length}");
                    if (encryptedBytes.Length == 0)
                    {
                        throw new InvalidOperationException("Encrypted data is empty or invalid.");
                    }

                    // TODO P2 remove temporary test to compare encrypt and decrypt
                    var decryptedPasscode = await interopModule!.InvokeAsync<string>("decryptWithNounce", encryptKeyBase64, encryptedPasscodeBase64);
                    byte[] dataBytes = Convert.FromBase64String(decryptedPasscode);
                    string plainTextPasscode = Encoding.UTF8.GetString(dataBytes);
                    // _logger.LogWarning("decryptedPasscode: {p} passcode {pp}", plainTextPasscode, passcode);
                    if (plainTextPasscode != passcode)
                    {
                        // _logger.LogError("passcode failed to encrypt-decrypt");
                        return Result.Fail("passcode failed to encrypt-decrypt");
                    }

                    // Append the new registered authenticator and prepare set for storage
                    var creationTime = DateTime.UtcNow;
                    var newRA = new Models.RegisteredAuthenticator()
                    {
                        CreationTime = creationTime,
                        LastUpdatedUtc = creationTime,
                        CredentialBase64 = credentialRet.Value.CredentialId,
                        EncryptedPasscodeBase64 = encryptedPasscodeBase64,
                        Name = $"Unnamed Authenticator"
                    };
                    registeredAuthenticators.Authenticators.Add(newRA);

                    // store updated registeredAuthenticators
                    await webExtensionsApi.Storage.Sync.Set(registeredAuthenticators);

                    // return the name of the newly added authenticatorRegistration
                    return Result.Ok(newRA.Name);
                }
                catch (JSException jsEx)
                {
                    throw new Exception(jsEx.Message);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                    return Result.Fail(ex.ToString());
                }
            }
            else
            {
                return Result.Fail("failed to retreive passcode from cache");
            }
        }

        public async Task<RegisteredAuthenticators> getRegisteredAuthenticators()
        {
            await Initialize();
            var webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
            var jsonElement = await webExtensionsApi.Storage.Sync.Get("authenticators"); // key matches name of property in RegisteredAuthenticators
            RegisteredAuthenticators ras = new();
            // if there are stored registered authenticators, start with that list
            RegisteredAuthenticators? t = JsonSerializer.Deserialize<RegisteredAuthenticators>(jsonElement, jsonSerializerOptions);
            if (t is not null)
            {
                ras = t;
            }
            else
            {
                ras = new RegisteredAuthenticators();
            }
            return ras;
        }

        public async Task<Result<string>> AuthenticateAKnownCredential()
        {
            await Initialize();
            RegisteredAuthenticators ras = await getRegisteredAuthenticators();

            if (ras is null || ras.Authenticators.Count == 0)
            {
                logger.LogWarning("no registered authenticators");
                return Result.Fail("no registered authenticators");
            }

            List<string> registeredCredIds = [];
            foreach (var registeredAuthenticator in ras.Authenticators)
            {
                registeredCredIds.Add(registeredAuthenticator.CredentialBase64);
            }

            var authenticateCredResult = await AuthenticateCredential(registeredCredIds);

            if (authenticateCredResult is null || authenticateCredResult.IsFailed)
            {
                logger.LogWarning("Failed to get result from authenticator");
                return Result.Fail("Failed to get result from authenticator");
            }
            // logger.LogWarning("success value: {s}", authenticateCredResult.Value);

            // Find the registered authenticator matching the credentialID, decrypt its encrypted passcode, and return that
            foreach (var registeredCred in ras.Authenticators) { 
                if (registeredCred.CredentialBase64 == authenticateCredResult.Value.CredentialId)
                {
                    try
                    {
                        string encryptKeyBase64 = getEncryptKeyBase64(authenticateCredResult.Value.EncryptKey);
                        var decryptedPasscode = await interopModule!.InvokeAsync<string>("decryptWithNounce", encryptKeyBase64, registeredCred.EncryptedPasscodeBase64);
                        // logger.LogWarning("decryptedPasscode {p}", decryptedPasscode);
                        byte[] decryptedPasscodeBytes = Convert.FromBase64String(decryptedPasscode);
                        string decryptedPlaintextPasscode = Encoding.UTF8.GetString(decryptedPasscodeBytes);
                        // logger.LogWarning("plainText {p}", decryptedPlaintextPasscode);
                        return Result.Ok(decryptedPlaintextPasscode);
                    }
                    catch
                    {
                        return Result.Fail("Could not decrypt");
                    }
                }
            }
            return Result.Fail($"Could not decrypt passcode based on webauthn credential");
        }
    }
}
