namespace KeriAuth.BrowserExtension.Services;

using FluentResults;
using KeriAuth.BrowserExtension.Models;


public interface IWalletService
{
    /// <summary>
    /// The properties stores the open, unencrypted wallet when the application is running
    /// </summary>
    public Wallet? Wallet { get; }

    /// <summary>
    /// Updates the current model of the open and unencrypted wallet and also stores and encrypts the wallet
    /// </summary>
    /// <param name="wallet"></param>
    /// <returns></returns>
    public Task SaveWallet(Wallet wallet);

    /// <summary>
    /// Loads the wallet from the storages and decrypts it
    /// </summary>
    /// <param name="walletPassword"></param>
    /// <returns></returns>
    public Task<Result> LoadWallet(string walletPassword);

    /// <summary>
    /// Deletes everything in storage
    /// </summary>
    /// <returns></returns>
    public Task DeleteWallet();

    /// <summary>
    /// Removes the quicklogin from storage and cleans up the in-memory entries
    /// </summary>
    /// <returns></returns>
    public Task CloseWallet();

    /// <summary>
    /// Tries to retrieve the wallet from storage. If the key is present and the retrieval is possible
    /// it returns true
    /// </summary>
    /// <returns></returns>
    public Task<bool> CheckIfWalletExists();

    /// <summary>
    /// Creates a quicklogin-class and stores it in storage
    /// </summary>
    /// <param name="password"></param>
    /// <returns></returns>
    public Task CreateQuickLogin(string password);

    /// <summary>
    /// Checks if the quicklogin objects exists inside the storage
    /// If so, it loads is and returns the key to decrypt the wallet
    /// </summary>
    /// <returns></returns>
    public Task<Result<string>> CheckQuickLogin();

    /// <summary>
    /// Returns the wallet a string, to be written to be stored as a backup
    /// </summary>
    /// <returns>json-string</returns>
    public Task<Result<string>> Backup();

    /// <summary>
    /// Restores the wallet from a matching json-string created from Backup()
    /// </summary>
    /// <param name="wallet"></param>
    /// <returns></returns>
    public Task<Result> Restore(WalletEncrypted wallet);


}