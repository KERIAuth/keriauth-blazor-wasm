using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models
{
    public class Salty
    {
        [JsonConstructor]
        public Salty(string sxlt, int pidx, int kidx, string stem, string tier, string dcode, List<string> icodes, List<string> ncodes, bool transferable)
        {
            Sxlt = sxlt;
            Pidx = pidx;
            Kidx = kidx;
            Stem = stem;
            Tier = tier;
            Dcode = dcode;
            Icodes = icodes;
            Ncodes = ncodes;
            Transferable = transferable;
        }
        [JsonPropertyName("sxlt")]
        public string Sxlt { get; init; }
        [JsonPropertyName("pidx")]
        public int Pidx { get; init; }
        [JsonPropertyName("kidx")]
        public int Kidx { get; init; }
        [JsonPropertyName("stem")]
        public string Stem { get; init; }
        [JsonPropertyName("tier")]
        public string Tier { get; init; }
        [JsonPropertyName("dcode")]
        public string Dcode { get; init; }
        [JsonPropertyName("icodes")]
        public List<string> Icodes { get; init; }
        [JsonPropertyName("ncodes")]
        public List<string> Ncodes { get; init; }
        [JsonPropertyName("transferable")]
        public bool Transferable { get; init; }
    }
}


