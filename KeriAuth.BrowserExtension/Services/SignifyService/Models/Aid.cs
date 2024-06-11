using System.Text.Json.Serialization;

namespace KeriAuth.BrowserExtension.Services.SignifyService.Models
{
    public class Aid
    {
        [JsonConstructor]
        public Aid(string name, string prefix, Salty salty)
        {
            Name = name;
            Prefix = prefix;
            Salty = salty;
        }
        [JsonPropertyName("name")]
        public string Name { get; init; }
        [JsonPropertyName("prefix")]
        public string Prefix { get; init; }
        [JsonPropertyName("salty")]
        public Salty Salty { get; init; }

        [JsonPropertyName("transferable")]
        public bool Transferable { get; init; }

        [JsonPropertyName("state")]
        public State State { get; init; }

        [JsonPropertyName("windexes")]
        public List<int> Windexes { get; init; }







    }
    /*  Example returned from signify_ts_shim.ts getAID(name) method:
        {
    "name": "gleif",
    "prefix": "EKgkNRu1ogZYBtnCsExp8a_ZB5BDRKFBwUqF_6bdPGwh",
    "salty": {
        "sxlt": "1AAHWr_qBwkmQSpALxpN3XkUIA3aNmvclDEchkE_QaTWiGolrAtYkM-4qF-Mtk_yIkzju_OrEuPwDukoI2d_0fjUkHxzWdri0pzk",
        "pidx": 0,
        "kidx": 0,
        "stem": "signify:aid",
        "tier": "low",
        "dcode": "E",
        "icodes": [
            "A"
        ],
        "ncodes": [
            "A"
        ],
        "transferable": true
    },
    "transferable": true,
    "state": {
        "vn": [
            1,
            0
        ],
        "i": "EKgkNRu1ogZYBtnCsExp8a_ZB5BDRKFBwUqF_6bdPGwh",
        "s": "2",
        "p": "ECT7d_uPLkGs2lfuyizd66YtXRkqo4py0w73Z3NQ6SdJ",
        "d": "EIOJm_BZt9Z0Vr7Wfc_E2iJZAc3bqfrsmbEAz4oNKLSN",
        "f": "2",
        "dt": "2024-06-10T20:19:35.062112+00:00",
        "et": "ixn",
        "kt": "1",
        "k": [
            "DIQ7PPMXHD5mfNCsdTovit2iSGc5CwSQ8SMul3ig9yB-"
        ],
        "nt": "1",
        "n": [
            "EBdywPOSmi5USLKxAaMZZixauROd9qMND9ab4egaBfNW"
        ],
        "bt": "3",
        "b": [
            "BBilc4-L3tFUnfM_wJr4S4OJanAv_VmF_dJNN6vkf2Ha",
            "BLskRTInXnMxWaGqcpSyMgo0nYbalW99cGZESrz3zapM",
            "BIKKuvBwpmDVA4Ds-EpL5bt9OqPzWPja2LigFYZN2YfX"
        ],
        "c": [],
        "ee": {
            "s": "0",
            "d": "EKgkNRu1ogZYBtnCsExp8a_ZB5BDRKFBwUqF_6bdPGwh",
            "br": [],
            "ba": []
        },
        "di": ""
    },
    "windexes": [
        0,
        1,
        2
    ]
}
     */
}
