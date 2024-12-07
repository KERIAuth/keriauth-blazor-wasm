namespace KeriAuth.BrowserExtension.Services;

using FluentResults;
using JsBind.Net;
using KeriAuth.BrowserExtension.Helper;
using KeriAuth.BrowserExtension.Models;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WebExtensions.Net;
using static KeriAuth.BrowserExtension.Services.IStorageService;
using JsonSerializer = System.Text.Json.JsonSerializer;

public partial class StorageService : IStorageService, IObservable<Preferences>
{
	private readonly IJSRuntime jsRuntime;
	private readonly IJsRuntimeAdapter jsRuntimeAdapter;
	private readonly ILogger<StorageService> logger;
	private readonly List<IObserver<Preferences>> preferencesObservers = [];
	private readonly WebExtensionsApi webExtensionsApi;

	public StorageService(IJSRuntime jsRuntime, IJsRuntimeAdapter jsRuntimeAdapter, ILogger<StorageService> logger)
	{
		this.jsRuntime = jsRuntime;
		this.jsRuntimeAdapter = jsRuntimeAdapter;
		this.logger = logger;
		logger.Log(ServiceLogLevel, "StorageService: constructor");
		_dotNetObjectRef = DotNetObjectReference.Create(this);
		webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);
	}

	public static LogLevel ServiceLogLevel { get; set; } = LogLevel.Debug;

	public delegate bool CallbackDelegate(object request, string something);

	private readonly DotNetObjectReference<StorageService>? _dotNetObjectRef;

	public void Dispose()
	{
		_dotNetObjectRef?.Dispose();
	}

	public async Task<Task> Initialize()
	{
		logger.Log(ServiceLogLevel, "Initialize");
		// This just needs to be done once after the service start up,
		try
		{
			logger.Log(ServiceLogLevel, "Using StorageApi");

			Debug.Assert(jsRuntimeAdapter is not null);
			logger.Log(ServiceLogLevel, "Registering handler for storage change event");

			// Set up to listen for storage changes.  Could alternately have implemented this in the background script and/or https://github.com/mingyaulee/WebExtensions.Net
			IJSObjectReference _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "/scripts/es6/storageHelper.js");
			await _module.InvokeVoidAsync("addStorageChangeListener", _dotNetObjectRef);
		}
		catch (Exception e)
		{
			// logger.LogError("Error adding eventListener to storage.onChange: {e}", e);
			throw new Exception("Error adding addStorageChangeListener", e);
		}
		logger.Log(ServiceLogLevel, "Added addStorageChangeListener");
		return Task.CompletedTask;
	}

	public AppHostingKind GetAppHostingKind()
	{
		return AppHostingKind.BlazorWasmHosted;
	}

	public async Task Clear()
	{
		await webExtensionsApi.Storage.Local.Clear();
		return;
	}

	public async Task RemoveItem<T>()
	{
		var tName = typeof(T).Name; //.ToUpperInvariant();
		await webExtensionsApi.Storage.Local.Remove(tName);
		return;
	}

	public async Task<Result<T?>> GetItem<T>()
	{
		try
		{
			var keys = new WebExtensions.Net.Storage.StorageAreaGetKeys(typeof(T).Name);
			var jsonElement = await webExtensionsApi.Storage.Local.Get(keys);
			if (jsonElement.TryGetProperty(Encoding.UTF8.GetBytes(typeof(T).Name), out JsonElement jsonElement2))
			{
				// logger.LogWarning("storageService22 value {x}", jsonElement2);
				// logger.LogWarning("storageService22 jsonElement {x}", jsonElement2);
				T? t = JsonSerializer.Deserialize<T>(jsonElement2, jsonSerializerOptions);
				// logger.LogError("storageService22 t: {x} .", t!.ToString());
				return t.ToResult<T?>();
			}
			else
			{
				// logger.LogError("storageService22 returning null");
				return Result.Ok();
			}

		}
		catch (Exception e)
		{
			logger.LogError("Failed to get item: {e}", e.Message);
			Console.WriteLine($"Failed to get item: {e.Message}");
			return Result.Fail($"Failed to get item: {e.Message}");
		}
	}

	private static readonly JsonSerializerOptions jsonSerializerOptions = new()
	{
		PropertyNameCaseInsensitive = false,
		IncludeFields = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
		//Converters =
		//{
		//    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		//}
	};

	public async Task<Result> SetItem<T>(T t)
	{
		try
		{
			var data = new Dictionary<string, object?>{
				{ typeof(T).Name, t }};
			await webExtensionsApi.Storage.Local.Set(data);
			return Result.Ok();
		}
		catch (Exception e)
		{
			var msg = "Failed to serialize or set item:";
			logger.LogError("{m} {e}", msg, e.Message);
			Console.WriteLine($"{msg} {e.Message}");
			return Result.Fail($"{msg} {e.Message}");
		}
	}
	IDisposable IObservable<Preferences>.Subscribe(IObserver<Preferences> preferencesObserver)
	{
		if (!preferencesObservers.Contains(preferencesObserver))
			preferencesObservers.Add(preferencesObserver);
		return new Unsubscriber(preferencesObservers, preferencesObserver);
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
            nameof(AppState)
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

		// Convert back to JSON string
		string filteredJson = JsonSerializer.Serialize(dict);
		return JsonDocument.Parse(filteredJson);
	}

	public async Task<Result<string>> GetBackupItems()
	{
		JsonDocument jsonDocument;
		try
		{
			Debug.Assert(jsRuntimeAdapter is not null);
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
				oldValue: kvp.Value.TryGetValue("oldValue", out JsonElement oldValue) ? (object)oldValue.ToString() : null,
				newValue: kvp.Value.TryGetValue("newValue", out JsonElement newValue) ? (object)newValue.ToString() : null
			)
		);

		logger.Log(ServiceLogLevel, "Storage changed: {changes}", convertedChanges);

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

						foreach (var observer in preferencesObservers)
						{
							observer.OnNext(preferences);
						}
					}
					// TODO P3 ALSO handle notifying subscribers for other keys
				}
				break;
			case "sync":
			case "managed":
			case "session":
			default:
				logger.LogError("Responding to storage area not implemented: {areaname}", areaname);
				break;
		}
	}
}