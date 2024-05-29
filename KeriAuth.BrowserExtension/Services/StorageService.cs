﻿namespace KeriAuth.BrowserExtension.Services;

using FluentResults;
using KeriAuth.BrowserExtension.Helper;
using KeriAuth.BrowserExtension.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebExtensions.Net;
using static KeriAuth.BrowserExtension.Services.IStorageService;
using JsonSerializer = System.Text.Json.JsonSerializer;

public partial class StorageService : IStorageService, IObservable<Preferences>
{
    private readonly IJSRuntime jsRuntime;  // TODO P2 in Playwright project, does this JsRuntime need to be null?  If not, then why is it nullable?
    private readonly ILogger<StorageService> logger;
    private readonly List<IObserver<Preferences>> preferencesObservers = [];

    public StorageService(IJSRuntime jsRuntime, ILogger<StorageService> logger)
    {
        this.jsRuntime = jsRuntime;
        this.logger = logger;
        this.logger.LogInformation("StorageService: constructor");
        _dotNetObjectRef = DotNetObjectReference.Create(this);
    }

    public delegate bool CallbackDelegate(object request, string something);

    private DotNetObjectReference<StorageService>? _dotNetObjectRef;
    // public event Action<Dictionary<string, (object oldValue, object newValue)>> OnStorageChanged;

    public void Dispose()
    {
        _dotNetObjectRef?.Dispose();
    }

    public async Task<Task> Initialize()
    {
        logger.LogInformation("Initialize");
        // This just needs to be done once after the services start up,
        try
        {
            logger.LogInformation("Using StorageApi");

            Debug.Assert(jsRuntime is not null);
            logger.LogInformation("Registering handler for storage change event");

            // Set up to listen for storage changes.  Could alternately have implemented this in the background script and/or https://github.com/mingyaulee/WebExtensions.Net
            // TODO investigate using https://github.com/mingyaulee/WebExtensions.Net
            IJSObjectReference _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "/scripts/storageHelper.js");
            await _module.InvokeVoidAsync("addStorageChangeListener", _dotNetObjectRef);
        }
        catch (Exception e)
        {
            // logger.LogError("Error adding eventListener to storage.onChange: {e}", e);
            throw new Exception("Error adding addStorageChangeListener", e);
        }
        logger.LogInformation("Added addStorageChangeListener");
        return Task.CompletedTask;
    }

    public AppHostingKind GetAppHostingKind()
    {
        return AppHostingKind.BlazorWasmHosted;
    }

    /// <inheritdoc />
    public async Task Clear()
    {
        Debug.Assert(jsRuntime is not null);
        await jsRuntime.InvokeVoidAsync("chrome.storage.local.clear");
        return;
    }

    /// <inheritdoc />
    public async Task RemoveItem<T>()
    {
        var tName = typeof(T).Name.ToUpperInvariant();
        Debug.Assert(jsRuntime is not null);
        await jsRuntime.InvokeVoidAsync("chrome.storage.local.remove", tName);
        return;
    }

    /// <inheritdoc />
    public async Task<Result<T?>> GetItem<T>()
    {
        try
        {
            logger.LogInformation("Getting item of type {name}", typeof(T).Name);

            T? nullValue = default;
            var tName = typeof(T).Name.ToUpperInvariant();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                // new IDataJsonConverter()
            }
            };
            JsonDocument jsonDocument;
            try
            {
                Debug.Assert(jsRuntime is not null);
                jsonDocument = await jsRuntime.InvokeAsync<JsonDocument>("chrome.storage.local.get", tName);
                logger.LogInformation("Got {doc}", jsonDocument.ToJsonString());
            }
            catch (Exception ex)
            {
                return Result.Fail($"Unable to access storage for key '{tName}': {ex.Message}");
            }

            if (jsonDocument is null)
            {
                return Result.Ok<T?>(nullValue);
            }

            // xxConsole.WriteLine($"Preparing to parse jsonDocument: {jsonDocument.ToJsonString()}");
            var parsedJsonNode = JsonNode.Parse(jsonDocument.ToJsonString());
            if (parsedJsonNode is null)
            {
                return Result.Fail($"Unable to parse jsonDocument: {jsonDocument.ToJsonString()}");
            }
            // xxConsole.WriteLine($"ParsedJsonNode: {parsedJsonNode.ToJsonString()}");
            var rootJsonNode = parsedJsonNode!.Root[tName];
            if (rootJsonNode is null)
            {
                // No content was found
                return Result.Ok<T?>(nullValue);
            }
            // xxConsole.WriteLine($"rootJsonNode: {rootJsonNode.ToJsonString()}");

            T? deserializedObject;
            try
            {
                // xxConsole.WriteLine($"prparing deserializedObject...");
                deserializedObject = JsonSerializer.Deserialize<T>(rootJsonNode.ToJsonString(), options);
                if (deserializedObject is null)
                {
                    // xxConsole.WriteLine($"deserializedObject is null");
                }
                else
                {
                    // xxConsole.WriteLine($"deserializedObject: {deserializedObject}");
                }
            }
            catch (Exception e)
            {
                return Result.Fail($"Failed to deserialize: {e.Message}");
            }

            if (deserializedObject is null)
            {
                // TODO P2 check if this is a success or failure?
                return Result.Fail($"");
            }

            // xxConsole.WriteLine($"preparing to return deserializedObject: {deserializedObject}");
            T? ret = deserializedObject;
            // xxConsole.WriteLine($"preparing to return deserializedObject: {ret}");
            return ret!.ToResult<T?>(); //  Result.Ok(ret); // ret; // Result.Ok(ret);
        }
        catch (Exception e)
        {
            // logger.LogError("Failed to get item: {e}", e.Message);
            Console.WriteLine($"Failed to get item: {e.Message}");
            return Result.Fail($"Failed to get item: {e.Message}");
        }
    }

    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            // new IDataJsonConverter()
        }
    };

    /// <inheritdoc />
    public async Task<Result> SetItem<T>(T t)
    {
        try
        {
            string jsonString;
            var tName = typeof(T).Name.ToUpperInvariant();
            try
            {
                jsonString = JsonSerializer.Serialize(t, jsonSerializerOptions);
            }
            catch (Exception e)
            {
                return Result.Fail($"Failed to serialize: {e.Message}");
            }

            try
            {
                Debug.Assert(jsRuntime is not null);
                object obj = await jsRuntime!.InvokeAsync<object>("JSON.parse", $"{{ \"{tName}\": {jsonString} }}");
                await jsRuntime!.InvokeVoidAsync("chrome.storage.local.set", obj);
            }
            catch (Exception e)
            {
                return Result.Fail($"Error writing to chrome.storage.local with key '{tName}': {e.Message}");
            }

            return Result.Ok();
        }
        catch (Exception e)
        {
            // logger.LogError("Failed to set item: {e}", e.Message);
            Console.WriteLine($"Failed to set item: {e.Message}");
            return Result.Fail($"Failed to set item: {e.Message}");
        }
    }

    IDisposable IObservable<Preferences>.Subscribe(IObserver<Preferences> preferencesObserver)
    {
        if (!preferencesObservers.Contains(preferencesObserver))
            preferencesObservers.Add(preferencesObserver);
        return new Unsubscriber(preferencesObservers, preferencesObserver);
    }

    // TODO Remove unused code Callback
    private bool Callback(object request, string _)
    {
        // When data has changed, so notifications and downstream effects might be needed

        // TODO P3 should there be try-catch blocks here, e.g. in case parsing fails?
        logger.LogInformation("In Callback");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                // new IDataJsonConverter()
            }
        };

        var jsonDocument = JsonDocument.Parse(request.ToString() ?? string.Empty);
        var parsedJsonNode = JsonNode.Parse(jsonDocument.ToJsonString());
        Debug.Assert(parsedJsonNode is not null);
        // TODO P3: an assumption below that there isn't more than one change
        var (key, value) = parsedJsonNode!.Root.AsObject().First();

        if (key == nameof(WalletEncrypted).ToUpperInvariant())
        {
            Debug.Assert(value is not null);
            var newWalletEncrypted = value["newValue"];
            if (newWalletEncrypted is not null)
            {
                var deserializedObject = JsonSerializer.Deserialize<WalletEncrypted>(newWalletEncrypted.ToJsonString(), options);
                // logger.LogInformation("new value for walletEncryped:");
                // xxConsole.WriteLine(deserializedObject);
            }
            else
            {
                // logger.LogInformation("new value for walletEncryped: null");
            }
        }
        else if (key.Equals(nameof(Preferences), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("In Callback ... Preferences");
            JsonNode? newJsonNode = null;
            if (value is not null)
            {
                // xxConsole.WriteLine($"value is: {value.ToString()}");
                newJsonNode = value["newValue"];
            }
            // xxConsole.WriteLine($"newJsonNode preferences is: {newJsonNode}");
            Preferences? newPreferences = null;
            if (newJsonNode is not null)
            {
                newPreferences = JsonSerializer.Deserialize<Preferences>(newJsonNode.ToJsonString(), options);
                Debug.Assert(newPreferences is not null);
                // xxConsole.WriteLine($"new value for Preferences IsDarkTheme: {newPreferences.IsDarkTheme}");
            }
            else
            {
                // logger.LogInformation("new value for Preferences: null");
            }
            // xxConsole.WriteLine($"There are {preferencesObservers.Count} preferencesObservers");

            if (preferencesObservers is not null && newPreferences is not null)
            {
                logger.LogInformation("Preferences updated: {value}", newPreferences.ToString());
                foreach (var observer in preferencesObservers)
                {
                    observer.OnNext(newPreferences);
                }
            }
        }
        return true;
    }

    private class Unsubscriber(List<IObserver<Preferences>> observers, IObserver<Preferences> observer) : IDisposable
    {
        private readonly List<IObserver<Preferences>> _preferencesObservers = observers;
        private readonly IObserver<Preferences> _preferencesObserver = observer;

        public void Dispose()
        {
            if (!(_preferencesObserver == null)) _preferencesObservers.Remove(_preferencesObserver);
        }
    }

    private JsonDocument RemoveKeys(JsonDocument jsonDocument)
    {
        List<string> topLevelKeysToRemove =
        [
            // These should not be backed up, in order to force a restore to reset the state of the wallet and authenticate.
            nameof(AppState).ToUpper(),
            nameof(WalletLogin).ToUpper(),
            nameof(BackupVersion).ToUpper()
        ];
        return RemoveKeys(jsonDocument, topLevelKeysToRemove);
    }

    private static JsonDocument RemoveKeys(JsonDocument jsonDocument, List<string> topLevelKeysToRemove)
    {
        // Convert to Dictionary
        Dictionary<string, JsonElement> dict = [];
        foreach (JsonProperty property in jsonDocument.RootElement.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }

        // Remove unwanted keys
        foreach (var key in topLevelKeysToRemove)
        {
            dict.Remove(key);
        }

        // TODO P2 indicate the version of the backupVersion, so we can handle changes in the future. Get the version and version_name from the manifest, if available
        BackupVersion backupVersion = new()
        {
            Version = "0.0.0",
            VersionName = "0.0.0 datetime commitHash",
            DateTime = DateTime.UtcNow.ToString("u")
        };
        dict.Add(nameof(BackupVersion).ToUpper(), JsonDocument.Parse(JsonSerializer.Serialize(backupVersion)).RootElement);

        // Convert back to JSON string
        string filteredJson = JsonSerializer.Serialize(dict);
        return JsonDocument.Parse(filteredJson);
    }

    public async Task<Result<string>> GetBackupItems()
    {
        JsonDocument jsonDocument;
        try
        {
            Debug.Assert(jsRuntime is not null);
            jsonDocument = await jsRuntime.InvokeAsync<JsonDocument>("chrome.storage.local.get", null);
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Unable to parse jsonDocument: {ex.Message}");
        }
        return Result.Ok(jsonDocument.ToJsonString());
    }

    [JSInvokable]
    public async Task NotifyStorageChanged(Dictionary<string, Dictionary<string, JsonElement>> changes, string areaname)
    {
        var convertedChanges = changes.ToDictionary(
            kvp => kvp.Key,
            kvp => (
                oldValue: kvp.Value.ContainsKey("oldValue") ? (object)kvp.Value["oldValue"].ToString() : null,
                newValue: kvp.Value.ContainsKey("newValue") ? (object)kvp.Value["newValue"].ToString() : null
            )
        );

        logger.LogInformation("Storage changed: {changes}", convertedChanges);

        switch (areaname)
        {
            case "local":
                if (changes is not null)
                {
                    if (changes.Keys.Contains(nameof(Preferences), StringComparer.OrdinalIgnoreCase))
                    {
                        // logger.LogWarning("Sending preferences to observer 111");
                        // will send the entire preferences to observers versus only the deltas
                        var res = await GetItem<Preferences>();
                        if (res.IsFailed)
                        {
                            logger.LogError("Failed to get preferences: {res}", res);
                            return;
                        }
                        var preferences = res.Value;

                        // logger.LogWarning("Sending preferences to observer 222");
                        if (preferences is not null)
                        {
                            foreach (var observer in preferencesObservers)
                            {
                                observer.OnNext(preferences);
                            }
                        }
                    }
                    // TODO ALSO handle notifying subscribers for other keys
                }
                break;
            case "sync":
            case "managed":
            case "session":
            default:
                logger.LogError("Responding to storage area not yet implemented: {areaname}", areaname);
                break;
        }
    }
}