
using KeriAuth.BrowserExtension.Models;
using FluentResults;
using System.Text.Json;
using WebExtensions.Net;

namespace KeriAuth.BrowserExtension.Services;
public interface IStorageService : IObservable<Preferences>
{
    Task<Task> Initialize(IWebExtensionsApi webExtensionsApi);

    /// <summary>
    /// Clear every entry in the storage to reset everything
    /// </summary>
    /// <returns></returns>
    Task Clear();

    /// <summary>
    /// Remove a specific object from the storage. eg. WalletLogin
    /// </summary>
    /// <typeparam name="T">Class which should be removed</typeparam>
    /// <returns></returns>
    Task RemoveItem<T>();

    /// <summary>
    /// Load a specific object from the storage e.g. the EncryptedWallet
    /// </summary>
    /// <typeparam name="T">Class which was stored</typeparam>
    /// <returns>A new instance of that class filled with the content from the storage</returns>
    public Task<Result<T?>> GetItem<T>();

    /// <summary>
    /// Stores a specific instance of a class in the storage
    /// </summary>
    /// <param name="t">Instance of the class to be stored</param>
    /// <typeparam name="T">Type of class</typeparam>
    /// <returns></returns>
    public Task<Result> SetItem<T>(T t);

    AppHostingKind GetAppHostingKind();

    public enum AppHostingKind
    {
        Unknown,
        BlazorWasmExtension,   // e.g. hosted via browser extension
        BlazorWasmHosted, // e.g. hosted by ASP.Net, IISExpress, Kestrel
        BlazorHybridApp  // e.g. for mobile app
    }

    public Task<Result<string>> GetBackupItems();
}