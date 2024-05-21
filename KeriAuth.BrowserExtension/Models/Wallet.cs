namespace KeriAuth.BrowserExtension.Models;

using System.Text.Json.Serialization;

public class Wallet
{
    public string CurrentClearTextPassword { get; private set; }
    // public List<WalletDid> WalletDids { get; private set; } = new();
    public DateTime CreatedUtc { get; private set; }

    [JsonConstructor]
    public Wallet(string currentClearTextPassword, DateTime createdUtc) //, List<WalletKeySecret> walletKeySecrets)
    {
        CurrentClearTextPassword = currentClearTextPassword;
        // WalletDids = walletDids;
        CreatedUtc = createdUtc;
        // WalletKeySecrets = walletKeySecrets;
    }

    public Wallet(string currentClearTextPassword )
    {
        if (string.IsNullOrEmpty(currentClearTextPassword) || currentClearTextPassword.Length < 4)
        {
            throw new Exception("Invalid password");
        }

        CreatedUtc = DateTime.UtcNow;
        // WalletDids = new List<WalletDid>();
        // AddDid(walletDid);
        CurrentClearTextPassword = currentClearTextPassword;
    }

    
}