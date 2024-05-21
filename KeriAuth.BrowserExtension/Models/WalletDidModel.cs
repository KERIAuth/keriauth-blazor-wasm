namespace KeriAuth.BrowserExtension.Models;

public class WalletDidModel
{
    public string? Name { get; set; }

    public DateTime CreatedUtc { get; set; }

    // public Did Did { get; set; }

    // public List<WalletCredential> WalletCredentials { get; set; }

    //[JsonConstructor]
    //public WalletDidModel(string name, DateTime createdUtc, Did did, List<WalletCredential> walletCredentials)
    //{
    //    Name = name;
    //    Did = did;
    //    CreatedUtc = createdUtc;
    //    WalletCredentials = walletCredentials;
    //}
}