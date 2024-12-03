using FluentResults;
using JsBind.Net;
using KeriAuth.BrowserExtension.Helper;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebExtensions.Net;
using KeriAuth.BrowserExtension.Models;
using static KeriAuth.BrowserExtension.UI.Pages.Authenticators;
using System.Security.Cryptography;
using static System.Runtime.InteropServices.JavaScript.JSType;
using WebExtensions.Net.ExtensionTypes;
using JsonSerializer = System.Text.Json.JsonSerializer;
using System.Net.NetworkInformation;
using System.Diagnostics;
using Blazor.BrowserExtension;
using Microsoft.AspNetCore.WebUtilities;
using System.Web;
using WebExtensions.Net.Runtime;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using static System.Net.WebRequestMethods;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MudBlazor;


namespace KeriAuth.BrowserExtension.Services
{
    public class WebauthnService(IJSRuntime jsRuntime, IJsRuntimeAdapter jsRuntimeAdapter, ILogger<WebauthnService> logger) : IWebauthnService
    {
        private readonly IJSRuntime _jsRuntime = jsRuntime;
        private static IJSObjectReference? _interopModule;
        private readonly IJsRuntimeAdapter _jsRuntimeAdapter = jsRuntimeAdapter;
        private readonly ILogger<WebauthnService> _logger = logger;

        async Task<IJSObjectReference> InitializeModule()
        {
            _interopModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/webauthnCredentialWithPRF.js");
            return _interopModule;
        }

        public async Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds)
        {
            try
            {
                // Attempt to call the JavaScript function and map to the CredentialWithPRF type
                var im = await InitializeModule();
                var credential = await im.InvokeAsync<CredentialWithPRF>("registerCredential", registeredCredIds);
                logger.LogWarning("credential: {c}", credential);
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

        public async Task<Result<string>> AuthenticateCredential(List<string> credentialIdBase64s)
        {
            logger.LogWarning("credentialIdBase64s: {r}", credentialIdBase64s);
            try
            {
                // Attempt to authenticate the credential and re-compute the encryption key
                var im = await InitializeModule();
                var encryptKeyBase64 = await im.InvokeAsync<string>("authenticateCredential", credentialIdBase64s);
                return Result.Ok(encryptKeyBase64);
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

        public async Task<Result<string>> RegisterAttestStoreAuthenticator()
        {
            // Get list of currently registered authenticators, so there isn't an attempt to create redundant credentials (i.e., same RP and user) on same authenticator
            var webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);  // TODO P2 why isn't this in the constructor?
            var jsonElement = await webExtensionsApi.Storage.Sync.Get("authenticators"); // key matches name of property in RegisteredAuthenticators
            RegisteredAuthenticators ras = new();
            // if there are stored registered authenticators, start with that list
            RegisteredAuthenticators? t = JsonSerializer.Deserialize<RegisteredAuthenticators>(jsonElement, jsonSerializerOptions);
            if (t is not null)
            {
                ras = t;
            }

            List<string> registeredCredIds = new();
            foreach (var registeredAuthenticator in ras.Authenticators)
            {
                registeredCredIds.Add(registeredAuthenticator.CredentialBase64);
            }

            // Register
            var credentialRet = await RegisterCredentialAsync(registeredCredIds);
            if (credentialRet is null || credentialRet.IsFailed)
            {
                return Result.Fail("Failed to register authenticator 333");
            }
            CredentialWithPRF credential = credentialRet.Value;

            string credentialIdBase64Url = credential.CredentialId;

            logger.LogWarning("credentialIdBase64: {b}", credentialIdBase64Url);
            List<string> xx = [];
            xx.Add(credentialIdBase64Url);

            // Get attestation from authenticator
            var encryptKeyBase64Ret = await AuthenticateCredential(xx);
            if (encryptKeyBase64Ret is null || encryptKeyBase64Ret.IsFailed)
            {
                return Result.Fail("Failed to verify with authenticator 444");
            }

            // Get the passcode from session storage
            var passcodeElement = await webExtensionsApi.Storage.Session.Get("passcode");
            if (passcodeElement.TryGetProperty("passcode", out JsonElement passcodeElement2) && passcodeElement2.ValueKind == JsonValueKind.String)
            {
                var passcode = passcodeElement2.GetString();
                if (passcode is null)
                {
                    return Result.Fail("no passcode is cached");
                }

                // Encrypt passcode
                var encryptKey = encryptKeyBase64Ret.Value;
                // _logger.LogWarning("encryptKey original length (chars): {b}", encryptKey.Length);

                // Step 1: Adjust the raw key to ensure 32 credentialIdBytes (256 bits)
                byte[] rawKeyBytes = Encoding.UTF8.GetBytes(encryptKey);

                // Truncate or pad the key to exactly 32 credentialIdBytes
                byte[] adjustedKeyBytes = new byte[32];
                Buffer.BlockCopy(rawKeyBytes, 0, adjustedKeyBytes, 0, Math.Min(rawKeyBytes.Length, 32));

                // Step 2: Convert the adjusted raw key to Base64
                string encryptKeyBase64 = Convert.ToBase64String(adjustedKeyBytes);
                // _logger.LogWarning("encryptKeyBase64 adjusted length (chars): {b}", encryptKeyBase64.Length);

                // Step 3: Convert the passcode to Base64
                byte[] plainBytes = Encoding.UTF8.GetBytes(passcode);
                string passcodeBase64 = Convert.ToBase64String(plainBytes);

                // Step 4: Call JavaScript function
                var im = await InitializeModule();
                var encryptedBase64 = await im.InvokeAsync<string>("encryptWithNounce", encryptKeyBase64, passcodeBase64);

                // Convert the Base64 string back to a byte array
                // byte[] encryptedPasscodeBytes = Convert.FromBase64String(encryptedBase64);
                // Log the result or use it // TODO P0 remove
                // _logger.LogWarning("Encrypted Passcode Bytes: {b}", BitConverter.ToString(encryptedPasscodeBytes));


                var keyBytes = Convert.FromBase64String(encryptKeyBase64);
                Console.WriteLine($"Key length in bytes: {keyBytes.Length}");
                if (keyBytes.Length != 16 && keyBytes.Length != 32)
                {
                    throw new InvalidOperationException("Encryption key must be 16 or 32 bytes.");
                }

                var encryptedBytes = Convert.FromBase64String(encryptedBase64);
                Console.WriteLine($"Encrypted data length: {encryptedBytes.Length}");
                if (encryptedBytes.Length == 0)
                {
                    throw new InvalidOperationException("Encrypted data is empty or invalid.");
                }

                var decryptedPasscode = await im.InvokeAsync<string>("decryptWithNounce", encryptKeyBase64, encryptedBase64);

                // TODO P0 tmp test to compare encrypt and decrypt

                // Step 1: Decode the Base64 string into a byte array
                byte[] dataBytes = Convert.FromBase64String(decryptedPasscode);

                // Step 2: Convert the byte array into a plaintext string
                string plainText = Encoding.UTF8.GetString(dataBytes);


                // _logger.LogWarning("decryptedPasscode: {p} passcode {pp}", plainText, passcode);
                if (plainText != passcode)
                {
                    // _logger.LogError("passcode failed to encrypt-decrypt");
                    return Result.Fail("passcode failed to encrypt-decrypt");
                }
                else
                {
                    // _logger.LogWarning("encrypt-decrypt passed");
                }

                // append the new registered authenticator to prepare for storage
                var creationTime = DateTime.UtcNow;
                var newRA = new Models.RegisteredAuthenticator()
                {
                    CreationTime = creationTime,
                    LastUpdatedUtc = creationTime,
                    CredentialBase64 = credentialRet.Value.CredentialId,
                    EncryptedPasscodeBase64 = encryptedBase64,
                    Name = $"Authenticator registered on {creationTime:R}"
                };
                ras.Authenticators.Add(newRA);

                // store it
                await webExtensionsApi.Storage.Sync.Set(ras);

                return Result.Ok(newRA.Name);
            }
            else
            {
                return Result.Fail("failed to retreive passcode from cache");
            }
        }
    }
}
