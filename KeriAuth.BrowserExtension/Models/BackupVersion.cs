namespace KeriAuth.BrowserExtension.Models
{
    using System.Text.Json.Serialization;
    public class BackupVersion
    {
        [JsonPropertyName("ver")]
        public string Version { get; set; } = "0.0.0";

        [JsonPropertyName("verName")]
        public string VersionName { get; set; } = "0.0.0 DateTime commitHash";

        [JsonPropertyName("datetime")]
        public string DateTime { get; set; } = "";
    }
}