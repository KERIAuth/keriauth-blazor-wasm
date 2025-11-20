// using System.Globalization;
using System.Text.Json.Serialization;
using Extension.Models.Storage;
using FluentResults;

namespace Extension.Models {
    public record KeriaConnectConfig : IStorageModel {
        [JsonConstructor]
        public KeriaConnectConfig(string? providerName = null, string? adminUrl = null, string? bootUrl = null, int passcodeHash = 0, string? clientAidPrefix = null, string? agentAidPrefix = null) {
            ProviderName = providerName;
            Alias = ""; // + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "UTC";
            AdminUrl = adminUrl;
            BootUrl = bootUrl;
            PasscodeHash = passcodeHash;
            ClientAidPrefix = clientAidPrefix;
            AgentAidPrefix = agentAidPrefix;
        }

        [JsonPropertyName("AdminUrl")]
        public string? AdminUrl { get; init; }

        [JsonPropertyName("BootUrl")]
        public string? BootUrl { get; init; }

        [JsonPropertyName("ProviderName")]
        public string? ProviderName { get; init; }

        [JsonPropertyName("Alias")]
        public string? Alias { get; init; }

        [JsonPropertyName("PasscodeHash")]
        public int PasscodeHash { get; init; }

        [JsonPropertyName("ClientAidPrefix")]
        public string? ClientAidPrefix { get; init; }

        [JsonPropertyName("AgentAidPrefix")]
        public string? AgentAidPrefix { get; init; }

        public Result<bool> ValidateConfiguration() {
            var errors = new List<IError>();
            
            if (string.IsNullOrEmpty(Alias)) {
                errors.Add(new ValidationError("Alias", "Alias is required"));
            }
            
            if (PasscodeHash == 0) {
                errors.Add(new ValidationError("PasscodeHash", "Passcode hash is missing"));
            }
            
            if (string.IsNullOrEmpty(AdminUrl)) {
                errors.Add(new ValidationError("AdminUrl", "Admin URL is required"));
            }
            // TODO P2 need to validate this construction is correct
            else if (!Uri.TryCreate(AdminUrl, UriKind.Absolute, out Uri? adminUriResult) 
                     || (adminUriResult.Scheme != Uri.UriSchemeHttp && adminUriResult.Scheme != Uri.UriSchemeHttps)) {
                errors.Add(new ValidationError("AdminUrl", "Admin URL must be a valid HTTP or HTTPS URL"));
            }
            
            if (errors.Count > 0) {
                return Result.Fail<bool>(errors);
            }
            
            return Result.Ok(true);
        }
    }
}
