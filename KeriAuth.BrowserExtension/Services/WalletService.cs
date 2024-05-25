namespace KeriAuth.BrowserExtension.Services;

using KeriAuth.BrowserExtension.Models;
using FluentResults;
using System.Diagnostics;
using System.Text.Json;

public class WalletService(IStorageService storageService, ILogger<WalletService> logger) : IWalletService
{
    // TODO user System.Security.Cryptography AesGcm.
    // private readonly IAesService _aesEncryptionService;
    // private readonly ISha256Service _sha256Service;
    private readonly IStorageService _storageService = storageService;
    // private const string PasswordSeed = "WalletPasswordSeed";
    // private const string QuickLoginPassword = "W8Rm12v2izJJ3d";
    private bool _isStoringWallet;

    private Wallet? _wallet;

    public Wallet? Wallet
    {
        get
        {
            if (_isStoringWallet)
                throw new Exception("attempted to fetch Wallet before save finished");
            return _wallet;
        }
    }

    //private async Task SetAndSaveWallet(Wallet value)
    //{
    //    await SaveWallet(value!);
    //    _wallet = value;
    //}

    /// <inheritdoc />
    public async Task SaveWallet(Wallet wallet)
    {
        await Task.Delay(0); // hack
        //var walletEncrypted = SerializeAndEncryptWallet(wallet, _aesEncryptionService, _sha256Service);
        //if (walletEncrypted.IsFailed)
        //{
        //    await _journalService.Write(new SystemLogEntry(nameof(WalletService), SystemLogEntryType.WalletEncryptionFailed));
        //    throw new Exception("Failed SerializeAndEncryptWallet");
        //}

        //await _journalService.Write(new SystemLogEntry(nameof(WalletService), SystemLogEntryType.WalletEncrypted));
        //await _storageService.SetItem(walletEncrypted.Value);
    }

    /// <inheritdoc />
    public async Task<Result> LoadWallet(string walletPassword)
    {
        await Task.Delay(0); // hack
        //var walletEncrypted = await _storageService.GetItem<WalletEncrypted>();
        //if (walletEncrypted.IsFailed)
        //{
        //    //TODO P3 provide better error handling for LoadWallet fail
        //    await _journalService.Write(new SystemLogEntry(nameof(WalletService), SystemLogEntryType.WalletDecryptionFailed));
        //    return Result.Fail("Wallet not found");
        //}

        //Debug.Assert(walletEncrypted.Value is not null);
        //var encryptionResult = DecryptAndDeserializeWallet(walletEncrypted.Value, walletPassword, _aesEncryptionService, _sha256Service);
        //if (encryptionResult.IsFailed)
        //{
        //    //wrong pw? snackbar?
        //    await _journalService.Write(new SystemLogEntry(nameof(WalletService), SystemLogEntryType.WalletDecryptionFailed));
        //    return Result.Fail("Invalid password");
        //}

        //await _journalService.Write(new SystemLogEntry(nameof(WalletService), SystemLogEntryType.WalletDecrypted));
        //await SetAndSaveWallet(encryptionResult.Value);
        //_messageRetrievalService.StartTimer();
        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<bool> CheckIfWalletExists()
    {
        await Task.Delay(0); // hack
        // TODO EE!
        //TODO P2 possible improvement: don't load the complete wallet, but just check if the key is present
        //var walletEncrypted = await _storageService.GetItem<WalletEncrypted>();
        //if (walletEncrypted.IsFailed)
        //{
        //    // a error occured. This should never happen
        //    return false;
        //}

        //if (walletEncrypted.ValueOrDefault is null)
        //{
        //    // no wallet in storage
        //    return false;
        //}

        return false;
    }

    /// <inheritdoc />
    public async Task CreateQuickLogin(string password)
    {
        await Task.Delay(0); // hack
        //var seedPhrase = "quickLoginSeedPhrase";
        //var dateTime = DateTime.UtcNow.ToBinary();
        //var quickLoginString = String.Concat(seedPhrase, "|", dateTime, "|", password);
        //var normalizedKey = new Hash(_sha256Service).Of(PrismEncoding.Utf8StringToByteArray(QuickLoginPassword));
        //Debug.Assert(normalizedKey is not null);
        //Debug.Assert(normalizedKey.Value is not null);
        //var encryptedQuickLoginString = _aesEncryptionService.Encrypt(quickLoginString, normalizedKey.Value); // the privateKey has to have a length of 32 bytes
        //var walletLogin = new WalletLogin(encryptedQuickLoginString);
        //await _storageService.SetItem(walletLogin);
    }

    /// <inheritdoc />
    public async Task<Result<string>> CheckQuickLogin()
    {
        await Task.Delay(0); // hack
        //var walletLogin = await _storageService.GetItem<WalletLogin>();
        //if (walletLogin.IsFailed)
        //{
        //    // an error occured, that should never happen
        //    return Result.Fail("Error");
        //}

        //if (walletLogin.ValueOrDefault is null)
        //{
        //    return Result.Fail("WalletLogin could not be found in storage");
        //}

        //if (walletLogin.Value is null || string.IsNullOrEmpty(walletLogin.Value.EncryptedLogin))
        //{
        //    return Result.Fail("QuickLogin could not be found");
        //}

        //var normalizedKey = new Hash(_sha256Service).Of(PrismEncoding.Utf8StringToByteArray(QuickLoginPassword)); // the privateKey has to have a length of 32 bytes
        //var decryptedQuickLoginString = _aesEncryptionService.Decrypt(walletLogin.Value.EncryptedLogin, normalizedKey.Value!);
        //var split = decryptedQuickLoginString.Split('|');
        //if (split.Length != 3)
        //{
        //    return Result.Fail("QuickLogin has invalid format");
        //}

        //var dateTimeBinary = split[1];
        //var parsedDateTime = DateTime.FromBinary(long.Parse(dateTimeBinary));
        //// TODO P3 Reimplement inactivity timeout using Chrome.alarms? ... might be a more efficient way to implement this. See https://developer.chrome.com/docs/extensions/reference/alarms/
        //if (parsedDateTime.AddMinutes(GetWalletTimeoutMinutes()) < DateTime.UtcNow)
        //{
        //    return Result.Fail($"QuickLogin timed out since {parsedDateTime}");
        //}
        //// TODO P3 refresh the QuickLogin dateTime here if older than a minute?
        //// await _walletService.CreateQuickLogin(password);

        ////_messageRetrievalService.StartTimer();
        //return Result.Ok(split[2]);

        return Result.Ok("temporary");
        
    }

    /// <inheritdoc />
    public async Task DeleteWallet()
    {
        //_messageRetrievalService.StopTimer();
        await _storageService.Clear();
        _wallet = null;
        _isStoringWallet = false;
    }

    /// <inheritdoc />
    public async Task CloseWallet()
    {
        //_messageRetrievalService.StopTimer();
        //await _storageService.RemoveItem<WalletLogin>();
        //await _journalService.Write(new SystemLogEntry(nameof(WalletService), SystemLogEntryType.WalletClosed));
        _wallet = null;
        _isStoringWallet = false;
        await Task.Delay(0); // hack
    }

    //private static Result<WalletEncrypted> SerializeAndEncryptWallet(Wallet wallet, IAesService aesServiceEncryption, ISha256Service sha256Service)
    //{
    //    if (string.IsNullOrEmpty(wallet.CurrentClearTextPassword))
    //    {
    //        return Result.Fail("Invalid password");
    //    }

    //    try
    //    {
    //        var serializedWallet = JsonSerializer.Serialize(wallet);
    //        var privateKey = PrismEncoding.Utf8StringToByteArray(string.Concat(PasswordSeed, wallet.CurrentClearTextPassword));
    //        var normalizedKey = new Hash(sha256Service).Of(privateKey); // the privateKey has to have a length of 32 bytes
    //        Debug.Assert(normalizedKey is not null);
    //        Debug.Assert(normalizedKey.Value is not null);
    //        var encryptedContent = aesServiceEncryption.Encrypt(serializedWallet, normalizedKey.Value);
    //        return Result.Ok(new WalletEncrypted(encryptedContent));
    //    }
    //    catch (Exception)
    //    {
    //        return Result.Fail("Unable to encrypt wallet");
    //    }
    //}

    //private static Result<Wallet> DecryptAndDeserializeWallet(WalletEncrypted walletEncrypted, string walletPassword, IAesService aesServiceEncryption, ISha256Service sha256Service)
    //{
    //    if (string.IsNullOrEmpty(walletPassword))
    //    {
    //        return Result.Fail("Invalid password");
    //    }

    //    try
    //    {
    //        var privateKey = PrismEncoding.Utf8StringToByteArray(string.Concat(PasswordSeed, walletPassword));
    //        var normalizedKey = new Hash(sha256Service).Of(privateKey); // the privateKey has to have a length of 32 bytes
    //        Debug.Assert(normalizedKey is not null);
    //        Debug.Assert(normalizedKey.Value is not null);
    //        var decryptedDid = aesServiceEncryption.Decrypt(walletEncrypted.Content, normalizedKey.Value);
    //        var deserializedDid = JsonSerializer.Deserialize<Wallet>(decryptedDid);
    //        if (deserializedDid == null)
    //        {
    //            return Result.Fail("Deserialization error");
    //        }

    //        return Result.Ok(deserializedDid);
    //    }
    //    catch (Exception e)
    //    {
    //        return Result.Fail($"Unable to decrypt wallet: {e.Message}");
    //    }
    //}


    public async Task<Result<string>> Backup()
    {
        //var walletEncrypted = await _storageService.GetItem<WalletEncrypted>();
        //if (walletEncrypted.IsFailed)
        //{
        //    return Result.Fail("Error. Unable to find encrypted wallet");
        //}

        //var walletEncryptedSerialized = JsonSerializer.Serialize(walletEncrypted.Value, SerializationOptions.UnsafeRelaxedEscaping);
        //return Result.Ok(walletEncryptedSerialized);
        await Task.Delay(0); // hack
        return Result.Ok("temporary");
    }

    public async Task<Result> Restore(WalletEncrypted walletEncrypted)
    {
        await _storageService.SetItem(walletEncrypted);
        return Result.Ok();
    }
}