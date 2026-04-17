using System.Text.Json.Serialization;
using Extension.Models.Messages.AppBw;

namespace Extension.Models {
    public record CreateIpexTestPrefs {
        [JsonPropertyName("IsPresentation")]
        public bool IsPresentation { get; init; }

        [JsonPropertyName("DiscloserPrefix")]
        public string? DiscloserPrefix { get; init; }

        [JsonPropertyName("Workflow")]
        public IpexWorkflow? Workflow { get; init; }

        [JsonPropertyName("DiscloseePrefix")]
        public string? DiscloseePrefix { get; init; }

        [JsonPropertyName("RoleName")]
        public string RoleName { get; init; } = "";
    }

    public record IssueCredentialsTestPrefs {
        [JsonPropertyName("IssuerPrefix")]
        public string? IssuerPrefix { get; init; }

        [JsonPropertyName("HolderPrefix")]
        public string? HolderPrefix { get; init; }

        [JsonPropertyName("RoleName")]
        public string RoleName { get; init; } = "";
    }

    public record PerConfigTestPrefs {
        [JsonPropertyName("CreateIpex")]
        public CreateIpexTestPrefs? CreateIpex { get; init; }

        [JsonPropertyName("IssueCredentials")]
        public IssueCredentialsTestPrefs? IssueCredentials { get; init; }
    }
}
