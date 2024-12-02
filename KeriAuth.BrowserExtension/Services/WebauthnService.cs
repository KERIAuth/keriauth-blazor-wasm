﻿using FluentResults;
using JsBind.Net;
using KeriAuth.BrowserExtension.Helper;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text;
using WebExtensions.Net;
using KeriAuth.BrowserExtension.Models;
using static KeriAuth.BrowserExtension.UI.Pages.Authenticators;
using System.Security.Cryptography;
using static System.Runtime.InteropServices.JavaScript.JSType;
using WebExtensions.Net.ExtensionTypes;


namespace KeriAuth.BrowserExtension.Services
{
    public class WebauthnService : IWebauthnService
    {
        private readonly IJSRuntime _jsRuntime;
        private IJSObjectReference _interopModule = default!;
        private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
        private readonly ILogger<WebauthnService> _logger;

        public WebauthnService(IJSRuntime jsRuntime, IJsRuntimeAdapter jsRuntimeAdapter, ILogger<WebauthnService> logger)
        {
            _jsRuntime = jsRuntime;
            _jsRuntimeAdapter = jsRuntimeAdapter;
            _logger = logger;
        }

        public async Task<Result<CredentialWithPRF>> RegisterCredentialAsync()
        {
            try
            {
                // Attempt to call the JavaScript function and map to the CredentialWithPRF type
                _interopModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/webauthnCredentialWithPRF.js");
                var credential = await _interopModule.InvokeAsync<CredentialWithPRF>("registerCredential");
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


        public async Task<Result<string>> AuthenticateCredential(string credentialIdBase64, string[] transports)
        {
            try
            {
                // Attempt to call the JavaScript function and map to the CredentialWithPRF type
                _interopModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/webauthnCredentialWithPRF.js");
                var encryptKeyBase64 = await _interopModule.InvokeAsync<string>("authenticateCredential", credentialIdBase64, transports);
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




        public async Task<Result<string>> RegisterAttestStoreAuthenticator(string passcode)
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

            // Register
            // TODO P1 pass in list of CredentialIDs of already created creds for this RP and User.
            var credentialRet = await RegisterCredentialAsync(/* credentials */);
            if (credentialRet is null || credentialRet.IsFailed)
            {
                return Result.Fail("Failed to register authenticator 333");
            }
            var credential = credentialRet.Value;

            // Get attestation from authenticator
            var encryptKeyBase64Ret = await AuthenticateCredential(credential.CredentialID, credential.Transports);
            if (encryptKeyBase64Ret is null || encryptKeyBase64Ret.IsFailed)
            {
                return Result.Fail("Failed to verify with authenticator 444");
            }




            // Encrypt passcode
            var encryptKey = encryptKeyBase64Ret.Value;
            _logger.LogWarning("encryptKey original length (chars): {b}", encryptKey.Length);

            // Step 1: Adjust the raw key to ensure 32 bytes (256 bits)
            byte[] rawKeyBytes = Encoding.UTF8.GetBytes(encryptKey);

            // Truncate or pad the key to exactly 32 bytes
            byte[] adjustedKeyBytes = new byte[32];
            Buffer.BlockCopy(rawKeyBytes, 0, adjustedKeyBytes, 0, Math.Min(rawKeyBytes.Length, 32));

            // Step 2: Convert the adjusted raw key to Base64
            string encryptKeyBase64 = Convert.ToBase64String(adjustedKeyBytes);
            _logger.LogWarning("encryptKeyBase64 adjusted length (chars): {b}", encryptKeyBase64.Length);

            // Step 3: Convert the passcode to Base64
            byte[] plainBytes = Encoding.UTF8.GetBytes(passcode);
            string passcodeBase64 = Convert.ToBase64String(plainBytes);

            // Step 4: Call JavaScript function
            var encryptedBase64 = await _interopModule.InvokeAsync<string>("encryptWithNounce", encryptKeyBase64, passcodeBase64);
            // Convert the Base64 string back to a byte array
            byte[] encryptedPasscodeBytes = Convert.FromBase64String(encryptedBase64);

            // Log the result or use it // TODO P0 remove
            _logger.LogWarning("Encrypted Passcode Bytes: {b}", BitConverter.ToString(encryptedPasscodeBytes));


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


            var decryptedPasscode = await _interopModule.InvokeAsync<string>("decryptWithNounce", encryptKeyBase64, encryptedBase64);





            // TODO P0 tmp test to compare encrypt and decrypt

            // Step 1: Decode the Base64 string into a byte array
            byte[] dataBytes = Convert.FromBase64String(decryptedPasscode);

            // Step 2: Convert the byte array into a plaintext string
            string plainText = Encoding.UTF8.GetString(dataBytes);


            _logger.LogWarning("decryptedPasscode: {p} passcode {pp}", plainText, passcode);
            if (plainText != passcode)
            {
                _logger.LogError("passcode failed to encrypt-decrypt");
                return Result.Fail("passcode failed to encrypt-decrypt");
            }
            else
            {
                _logger.LogWarning("encrypt-decrypt passed");
            }

            // append the new registered authenticator to prepare for storage
            var creationTime = DateTime.UtcNow;
            var newRA = new Models.RegisteredAuthenticator()
            {
                CreationTime = creationTime,
                LastUpdatedUtc = creationTime,
                CredentialBase64 = credentialRet.Value.CredentialID,
                EncryptedPasscodeBase64 = encryptedBase64,
                Name = $"Authenticator registered on {creationTime:R}"
            };
            ras.Authenticators.Add(newRA);

            // store it
            await webExtensionsApi.Storage.Sync.Set(ras);

            return Result.Ok(newRA.Name);
        }
    }

    /*
    public class SymmetricEncryption
    {
        // Encrypt a string using AES
        public static string EncryptString(string plainText, byte[] encryptionKey)
        {
            if (string.IsNullOrEmpty(plainText)) throw new ArgumentNullException(nameof(plainText));
            if (encryptionKey == null || encryptionKey.Length != 32) // AES-256 requires a 256-bit (32-byte) key
                throw new ArgumentException("Encryption key must be a 256-bit (32-byte) array.", nameof(encryptionKey));

            using Aes aes = Aes.Create();
            aes.Key = encryptionKey;
            aes.GenerateIV(); // Generate a random IV

            using MemoryStream memoryStream = new MemoryStream();
            // Write the IV at the beginning of the stream
            memoryStream.Write(aes.IV, 0, aes.IV.Length);

            using (CryptoStream cryptoStream = new(memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                cryptoStream.Write(plainBytes, 0, plainBytes.Length);
                cryptoStream.FlushFinalBlock();
            }

            // Convert the encrypted data and IV to a Base64 string
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        // Decrypt a string using AES
        public static string DecryptString(string cipherText, byte[] encryptionKey)
        {
            if (string.IsNullOrEmpty(cipherText)) throw new ArgumentNullException(nameof(cipherText));
            if (encryptionKey == null || encryptionKey.Length != 32) // AES-256 requires a 256-bit (32-byte) key
                throw new ArgumentException("Encryption key must be a 256-bit (32-byte) array.", nameof(encryptionKey));

            byte[] cipherBytes = Convert.FromBase64String(cipherText);

            using Aes aes = Aes.Create();
            aes.Key = encryptionKey;

            // Extract the IV from the beginning of the ciphertext
            byte[] iv = new byte[aes.BlockSize / 8];
            Array.Copy(cipherBytes, 0, iv, 0, iv.Length);
            aes.IV = iv;

            // Create a stream for the remaining ciphertext (after the IV)
            using MemoryStream memoryStream = new(cipherBytes, iv.Length, cipherBytes.Length - iv.Length);
            using CryptoStream cryptoStream = new(memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using StreamReader streamReader = new(cryptoStream, Encoding.UTF8);
            return streamReader.ReadToEnd();
        }
    }
    */


}
