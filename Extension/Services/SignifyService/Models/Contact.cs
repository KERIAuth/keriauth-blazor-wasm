using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public class Contact : Dictionary<string, object> {
        [JsonPropertyName("alias")]
        public string Alias { get; set; } = string.Empty;

        [JsonPropertyName("oobi")]
        public string Oobi { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        public Contact() { }

        public Contact(string alias, string oobi, string id) {
            Alias = alias;
            Oobi = oobi;
            Id = id;
            this["alias"] = alias;
            this["oobi"] = oobi;
            this["id"] = id;
        }
    }

    public class ContactInfo : Dictionary<string, object> {
        private string? _alias;
        private string? _oobi;

        [JsonPropertyName("alias")]
        public string? Alias { 
            get => _alias;
            set {
                _alias = value;
                if (value != null) {
                    this["alias"] = value;
                } else {
                    this.Remove("alias");
                }
            }
        }

        [JsonPropertyName("oobi")]
        public string? Oobi { 
            get => _oobi;
            set {
                _oobi = value;
                if (value != null) {
                    this["oobi"] = value;
                } else {
                    this.Remove("oobi");
                }
            }
        }

        public ContactInfo() { }

        public ContactInfo(string? alias = null, string? oobi = null) {
            if (alias != null) {
                Alias = alias;
            }
            if (oobi != null) {
                Oobi = oobi;
            }
        }
    }
}
