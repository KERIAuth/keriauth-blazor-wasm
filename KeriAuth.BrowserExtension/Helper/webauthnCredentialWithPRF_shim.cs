using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using FluentResults;
using System.Threading.Tasks;
using System.Text.Json;
using System.ComponentModel.Design;
using System.Linq;

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

        [JSImport("registerCredentialJ", "webauthnCredentialWithPRF")]
        internal static partial Task<string> RegisterCredentialJ();

        public static async Task<Result> RegisterCredential()
        {
            string json = await RegisterCredentialJ();
            Console.WriteLine($"json: {json}");

            // Ensure the JSON is not null or empty before deserialization
            if (string.IsNullOrWhiteSpace(json))
            {
                return Result.Fail(new FluentResults.Error("Received an empty or invalid response from the JavaScript function."));
            }

            try
            {
                // Deserialize into an intermediate class
                var intermediateResult = JsonSerializer.Deserialize<IntermediateResult>(json);
                if (intermediateResult is null)
                {
                    return Result.Fail(new FluentResults.Error("Deserialization resulted in a null object."));
                }
                Console.WriteLine($"intermediateResult: {intermediateResult.ToString()}");
                Console.WriteLine($"intermediateResult: {intermediateResult.ToResult()}");

                // Convert intermediate result to FluentResults Result
                if (intermediateResult.IsSuccess)
                {
                    return Result.Ok();
                }
                else
                {
                    var errors = intermediateResult.Errors
                        .Select(e => new FluentResults.Error(e))
                        .ToList();
                    return Result.Fail(errors.FirstOrDefault());
                }
            }
            catch (JsonException jsonEx)
            {
                // Handle JSON-specific errors
                return Result.Fail(new FluentResults.Error($"JSON deserialization error: {jsonEx.Message}"));
            }
            catch (Exception ex)
            {
                // Handle any other unexpected errors
                return Result.Fail(new FluentResults.Error($"An unexpected error occurred: {ex.Message}"));
            }
        }

        [JSImport("authenticateCredential", "webauthnCredentialWithPRF")]
        internal static partial Task<string> AuthenticateCredential();
    }

    // Intermediate class to match the JSON structure
    class IntermediateResult
    {
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = new();
    }

}

