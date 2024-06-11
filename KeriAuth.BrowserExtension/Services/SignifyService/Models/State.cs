using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Services.SignifyService.Models
{
    public class State
    {
        public Agent? Agent { get; }
        public Controller? Controller { get; }
        public int Ridx { get; }
        public int Pidx { get; }
    }


    /*
    public class State
    {
        [JsonConstructor]
        public State(List<int> vn, string i, string s, string p, string d, string f, string dt, string et, string kt, List<string> k, string nt, List<string> n, string bt, List<string> b, List<string> c, Ee ee, string di)
        {
            Vn = vn;
            I = i;
            S = s;
            P = p;
            D = d;
            F = f;
            Dt = dt;
            Et = et;
            Kt = kt;
            K = k;
            Nt = nt;
            N = n;
            Bt = bt;
            B = b;
            C = c;
            Ee = ee;
            Di = di;
        }
        [JsonPropertyName("vn")]
        public List<int> Vn { get; init; }
        [JsonPropertyName("i")]
        public string I { get; init; }
        [JsonPropertyName("s")]
        public string S { get; init; }
        [JsonPropertyName("p")]
        public string P { get; init; }
        [JsonPropertyName("d")]
        public string D { get; init; }
        [JsonPropertyName("f")]
        public string F { get; init; }
        [JsonPropertyName("dt")]
        public string Dt { get; init; }
        [JsonPropertyName("et")]
        public string Et { get; init; }
        [JsonPropertyName("kt")]
        public string Kt { get; init; }
        [JsonPropertyName("k")]
        public List<string> K { get; init; }
        [JsonPropertyName("nt")]
        public string Nt { get; init; }
        [JsonPropertyName("n")]
        public List<string> N { get; init; }
        [JsonPropertyName("bt")]
        public string Bt { get; init; }
        [JsonPropertyName("b")]
        public List<string> B { get; init; }
        [JsonPropertyName("c")]
        public List<string> C { get; init; }
        [JsonPropertyName("ee")]
        public Ee Ee { get; init; }
        [JsonPropertyName("di")]
        public string Di { get; init; }
    }
    */
}
