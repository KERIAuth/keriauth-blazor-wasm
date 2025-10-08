# Refactoring Plan: Command + Reactive Storage Architecture

## Document Version
- **Created**: 2025-01-08
- **Status**: Planning Phase
- **Last Updated**: 2025-01-08

---

## Executive Summary

This document outlines the architectural refactoring to centralize SignifyClient (KERIA communication) in the BackgroundWorker, with the App UI layer adopting a command + reactive storage pattern.

### Current Problem
- BackgroundWorker and App run as **separate Blazor WASM runtime instances**
- Each has its own SignifyClient module instance
- When App connects to KERIA and closes, BackgroundWorker loses connection
- Result: "SignifyClient not connected" errors when BackgroundWorker tries to handle sign requests

### Target Architecture
- **Single KERIA Connection**: BackgroundWorker maintains the only connection
- **Command Flow Down**: App → BackgroundWorker via port messages (imperative)
- **Data Flow Up**: BackgroundWorker → Session Storage → App (reactive)
- **Reactive UI**: App pages subscribe to storage changes and update automatically

---

## Architecture Philosophy

### Commands Flow Down (App → BackgroundWorker)
- App sends imperative commands via port messages
- BackgroundWorker executes commands and updates storage
- Command responses are optional (fire-and-forget where appropriate)

### Data Flows Up (BackgroundWorker → Storage → App)
- BackgroundWorker writes to `chrome.storage.session` after operations
- App pages reactively subscribe to storage changes
- UI updates automatically when data changes
- Multiple App windows/tabs stay synchronized automatically

---

## Phase 1: Analyze Existing Patterns ✅

### 1.1 Current Storage Usage

**Already Reactive (via StateService):**
- `AppState` - Stored in local storage, observed via IObservable pattern
- State machine transitions: `Uninitialized` → `Initializing` → `Unconfigured`/`Unauthenticated` → `AuthenticatedDisconnected`/`AuthenticatedConnected`

**Already in Session Storage:**
- `passcode` - Cached passcode for 5-minute window
- Cleared on lock/timeout via `StateService.TimeOut()`

**Preferences Observable:**
- `Preferences` - Stored in local storage
- Already has IObservable implementation in StorageService (line 226-230)

**Existing Storage Listener:**
- [Extension/wwwroot/scripts/es6/storageHelper.ts](Extension/wwwroot/scripts/es6/storageHelper.ts) - Listens to `chrome.storage.onChanged` for **local** storage
- StorageService.NotifyStorageChanged handles Preferences updates

### 1.2 Missing Reactive Data

**Need to Add to Session Storage:**
- `Identifiers` (AIDs list) - Currently fetched on-demand
- `Credentials` (ACDCs list) - Currently fetched on-demand
- `KeriaConnectionState` - Connected/Disconnected/Connecting/Error
- `CurrentAids` - Filtered/sorted list for UI (future)

### 1.3 Current SignifyClientService Usage

**Pages using ISignifyClientService (22 total):**
- ConfigurePage.razor
- UnlockPage.razor
- Index.razor
- IdentifiersPage.razor
- CredentialsPage.razor
- DashboardPage.razor
- WelcomePage.razor
- KeriAgentServicePage.razor
- WebsitePage.razor
- WebsitesPage.razor
- IdentifierPage.razor
- RequestSignInPage.razor
- RequestSignPage.razor
- ConnectingPage.razor
- AddAuthenticatorPage.razor
- Authenticators.razor
- PrivacyPage.razor
- TermsPage.razor
- NewReleasePage.razor
- WebsiteConfigDisplay.razor (component)
- MainLayout.razor
- BaseLayout.razor

**Common Operations:**
- `GetIdentifiers()` - 6 usages
- `GetCredentials()` - 4 usages
- `Connect()` - 3 usages
- `RunCreateAid()` - 2 usages
- `HealthCheck()` - 1 usage

---

## Phase 2: Design Session Storage Schema

### 2.1 Session Storage Keys

```typescript
// Session storage schema (chrome.storage.session)
{
  // Already exists
  "passcode": string | null,  // 21-char passcode, cleared after 5 min

  // New additions
  "keriaConnectionState": {
    "status": "disconnected" | "connecting" | "connected" | "error",
    "timestamp": number,  // Unix timestamp (milliseconds)
    "error": string | null
  },

  "identifiers": {
    "aids": Array<{
      "name": string,
      "prefix": string,
      "salty": any,
      // ... other Aid properties from signify-ts
    }>,
    "timestamp": number,  // Last update time
    "stale": boolean  // True if needs refresh
  },

  "credentials": {
    "acdc": Array<{
      "sad": RecursiveDictionary,
      "schema": string,
      "status": any,
      // ... other credential properties
    }>,
    "timestamp": number,
    "stale": boolean
  }
}
```

### 2.2 Local Storage Schema (Existing, for reference)

```typescript
// Local storage (chrome.storage.local)
{
  "AppState": {
    "CurrentState": "AuthenticatedConnected" | "AuthenticatedDisconnected" | "Unauthenticated" | "Unconfigured" | "Initializing" | "Uninitialized"
  },
  "KeriaConnectConfig": {
    "AdminUrl": string,
    "BootUrl": string,
    "ProviderName": string,
    "Alias": string,
    "PasscodeHash": number,
    "ClientAidPrefix": string,
    "AgentAidPrefix": string
  },
  "Preferences": { /* user preferences */ },
  "WebsiteConfigs": { /* per-origin configs */ }
}
```

### 2.3 Data Staleness Strategy

**When to mark data as stale:**
- After N minutes (e.g., identifiers stale after 30 minutes)
- After operations that may change data (e.g., CreateAid → mark identifiers stale)
- On reconnection to KERIA

**How to handle stale data:**
- Pages can display stale data immediately (better UX)
- Pages trigger background refresh automatically
- Show "Refreshing..." indicator if data is stale

---

## Phase 3: Command Protocol (App → BackgroundWorker)

### 3.1 Command Message Types

**New file: `Extension/Models/BwCommands/BwCommand.cs`**

```csharp
public abstract record BwCommand(string CommandId);

// Connection commands
public record ConnectToKeriaCommand(
    string CommandId,
    string Url,
    string Passcode,
    string? BootUrl,
    bool IsBootForced = true
) : BwCommand(CommandId);

public record DisconnectFromKeriaCommand(string CommandId) : BwCommand(CommandId);

// Data refresh commands
public record RefreshIdentifiersCommand(string CommandId) : BwCommand(CommandId);

public record RefreshCredentialsCommand(
    string CommandId,
    string? AidName = null,
    string? Issuer = null
) : BwCommand(CommandId);

// AID management commands
public record CreateAidCommand(
    string CommandId,
    string Name,
    string? Algo = null,
    int? Count = null
) : BwCommand(CommandId);

// Future commands (placeholder)
// public record RotateKeysCommand(string CommandId, string AidName) : BwCommand(CommandId);
// public record IssueCredentialCommand(...) : BwCommand(CommandId);
// public record RevokeCredentialCommand(...) : BwCommand(CommandId);
```

### 3.2 Command Response (Optional)

```csharp
// Only for commands that need immediate feedback
// Most commands are fire-and-forget; UI updates via storage
public record BwCommandResponse(
    string CommandId,
    bool Success,
    string? Error = null,
    object? Data = null  // Optional immediate data
);
```

### 3.3 Message Type Constants

**Update `Extension/Models/ExCsMessages/BwCsMsgTypes.cs`:**

```csharp
public static class BwCsMsgTypes {
    // ... existing types (POLARIS_*, REPLY, etc.)

    // Commands (App → BackgroundWorker)
    public const string CMD_CONNECT_KERIA = "/command/connect-keria";
    public const string CMD_DISCONNECT_KERIA = "/command/disconnect-keria";
    public const string CMD_REFRESH_IDENTIFIERS = "/command/refresh-identifiers";
    public const string CMD_REFRESH_CREDENTIALS = "/command/refresh-credentials";
    public const string CMD_CREATE_AID = "/command/create-aid";

    // Command responses (BackgroundWorker → App) - optional
    public const string CMD_RESPONSE = "/command/response";
}
```

---

## Phase 4: BackgroundWorker Command Handlers

### 4.1 Command Handler Partial Class

**New file: `Extension/BackgroundWorker.Commands.cs`**

```csharp
public partial class BackgroundWorker {

    /// <summary>
    /// Connect to KERIA and update session storage with connection state
    /// </summary>
    private async Task HandleConnectToKeriaCommand(ConnectToKeriaCommand cmd, Port appPort) {
        try {
            _logger.LogInformation("BW HandleConnectToKeriaCommand: URL={Url}", cmd.Url);

            // Update connection state: connecting
            await UpdateKeriaConnectionState("connecting", null);

            // Attempt connection in BackgroundWorker's SignifyClient context
            var result = await _signifyService.Connect(
                cmd.Url,
                cmd.Passcode,
                cmd.BootUrl,
                cmd.IsBootForced
            );

            if (result.IsSuccess) {
                _logger.LogInformation("BW Connected to KERIA successfully");

                // Update connection state: connected
                await UpdateKeriaConnectionState("connected", null);

                // Automatically refresh identifiers after successful connection
                await RefreshIdentifiersToStorage();

                // Optional: Send success response
                SendCommandResponse(new BwCommandResponse(cmd.CommandId, true), appPort);
            } else {
                _logger.LogWarning("BW KERIA connection failed: {Error}", result.Errors[0].Message);

                // Update connection state: error
                await UpdateKeriaConnectionState("error", result.Errors[0].Message);

                // Send error response
                SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, result.Errors[0].Message), appPort);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW Error handling ConnectToKeria command");
            await UpdateKeriaConnectionState("error", ex.Message);
            SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, ex.Message), appPort);
        }
    }

    /// <summary>
    /// Disconnect from KERIA and update session storage
    /// </summary>
    private async Task HandleDisconnectFromKeriaCommand(DisconnectFromKeriaCommand cmd, Port appPort) {
        try {
            _logger.LogInformation("BW HandleDisconnectFromKeriaCommand");

            // Disconnect SignifyClient
            // TODO P1: Implement disconnect method in ISignifyClientService
            // await _signifyService.Disconnect();

            // Update connection state
            await UpdateKeriaConnectionState("disconnected", null);

            // Clear identifiers and credentials from session storage
            await WebExtensions.Storage.Session.Remove("identifiers");
            await WebExtensions.Storage.Session.Remove("credentials");

            SendCommandResponse(new BwCommandResponse(cmd.CommandId, true), appPort);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW Error handling DisconnectFromKeria command");
            SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, ex.Message), appPort);
        }
    }

    /// <summary>
    /// Refresh identifiers and write to session storage
    /// </summary>
    private async Task HandleRefreshIdentifiersCommand(RefreshIdentifiersCommand cmd, Port appPort) {
        try {
            _logger.LogInformation("BW HandleRefreshIdentifiersCommand");

            await RefreshIdentifiersToStorage();

            SendCommandResponse(new BwCommandResponse(cmd.CommandId, true), appPort);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW Error refreshing identifiers");
            SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, ex.Message), appPort);
        }
    }

    /// <summary>
    /// Refresh credentials and write to session storage
    /// </summary>
    private async Task HandleRefreshCredentialsCommand(RefreshCredentialsCommand cmd, Port appPort) {
        try {
            _logger.LogInformation("BW HandleRefreshCredentialsCommand: AidName={AidName}", cmd.AidName);

            var result = await _signifyService.GetCredentials(cmd.AidName, cmd.Issuer);

            if (result.IsSuccess) {
                var credentialsData = new {
                    acdc = result.Value.Acdc,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    stale = false
                };

                await WebExtensions.Storage.Session.Set(new { credentials = credentialsData });
                _logger.LogInformation("BW Credentials refreshed and written to session storage");

                SendCommandResponse(new BwCommandResponse(cmd.CommandId, true), appPort);
            } else {
                _logger.LogWarning("BW Failed to get credentials: {Error}", result.Errors[0].Message);
                SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, result.Errors[0].Message), appPort);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW Error refreshing credentials");
            SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, ex.Message), appPort);
        }
    }

    /// <summary>
    /// Create new AID and refresh identifiers list
    /// </summary>
    private async Task HandleCreateAidCommand(CreateAidCommand cmd, Port appPort) {
        try {
            _logger.LogInformation("BW HandleCreateAidCommand: Name={Name}", cmd.Name);

            var result = await _signifyService.RunCreateAid(cmd.Name, cmd.Algo, cmd.Count);

            if (result.IsSuccess) {
                _logger.LogInformation("BW AID created successfully: {Name}", cmd.Name);

                // Refresh identifiers list after creating new AID
                await RefreshIdentifiersToStorage();

                SendCommandResponse(new BwCommandResponse(cmd.CommandId, true, Data: result.Value), appPort);
            } else {
                _logger.LogWarning("BW Failed to create AID: {Error}", result.Errors[0].Message);
                SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, result.Errors[0].Message), appPort);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW Error creating AID");
            SendCommandResponse(new BwCommandResponse(cmd.CommandId, false, ex.Message), appPort);
        }
    }

    // ===================== Helper Methods =====================

    /// <summary>
    /// Update KERIA connection state in session storage
    /// </summary>
    private async Task UpdateKeriaConnectionState(string status, string? error) {
        var connectionState = new {
            status,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            error
        };

        await WebExtensions.Storage.Session.Set(new { keriaConnectionState = connectionState });
        _logger.LogInformation("BW KERIA connection state updated: {Status}", status);
    }

    /// <summary>
    /// Refresh identifiers from KERIA and write to session storage
    /// </summary>
    private async Task RefreshIdentifiersToStorage() {
        var result = await _signifyService.GetIdentifiers();

        if (result.IsSuccess) {
            var identifiersData = new {
                aids = result.Value.Aids,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                stale = false
            };

            await WebExtensions.Storage.Session.Set(new { identifiers = identifiersData });
            _logger.LogInformation("BW Identifiers refreshed: {Count} AIDs written to session storage", result.Value.Aids.Count);
        }
        else {
            _logger.LogWarning("BW Failed to refresh identifiers: {Error}", result.Errors[0].Message);
        }
    }

    /// <summary>
    /// Send command response back to App
    /// </summary>
    private void SendCommandResponse(BwCommandResponse response, Port appPort) {
        try {
            var msg = new BwCsMsg(
                type: BwCsMsgTypes.CMD_RESPONSE,
                requestId: response.CommandId,
                payload: response
            );

            appPort.PostMessage(msg);
            _logger.LogDebug("BW Sent command response: CommandId={CommandId}, Success={Success}",
                response.CommandId, response.Success);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "BW Error sending command response");
        }
    }
}
```

### 4.2 Update Message Router

**In `Extension/BackgroundWorker.cs` → `SetUpBlazorAppMessageHandling`:**

```csharp
private void SetUpBlazorAppMessageHandling(WebExtensions.Net.Runtime.Port port, string connectionId, string portType) {
    // ... existing setup code ...

    port.OnMessage.AddListener(async (object message, MessageSender sender, Action sendResponse) => {
        _logger.LogInformation("BW🡠App message on port {ConnectionId}: {Message}", connectionId, message);

        try {
            await HandleMessageFromAppAsync(message, port, connectionId);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing Blazor App message on port {ConnectionId}", connectionId);
        }

        return false;
    });

    // ... rest of setup ...
}

private async Task HandleMessageFromAppAsync(object messageObj, WebExtensions.Net.Runtime.Port appPort, string appConnectionId) {
    try {
        // Deserialize the message
        var messageJson = JsonSerializer.Serialize(messageObj);
        var message = JsonSerializer.Deserialize<BwCsMsg>(messageJson, PortMessageJsonOptions);

        if (message == null) {
            _logger.LogWarning("Failed to deserialize message from Blazor App");
            return;
        }

        // Route based on message type
        switch (message.Type) {
            // New command handlers
            case BwCsMsgTypes.CMD_CONNECT_KERIA:
                var connectCmd = DeserializePayload<ConnectToKeriaCommand>(message.Payload);
                await HandleConnectToKeriaCommand(connectCmd, appPort);
                break;

            case BwCsMsgTypes.CMD_DISCONNECT_KERIA:
                var disconnectCmd = DeserializePayload<DisconnectFromKeriaCommand>(message.Payload);
                await HandleDisconnectFromKeriaCommand(disconnectCmd, appPort);
                break;

            case BwCsMsgTypes.CMD_REFRESH_IDENTIFIERS:
                var refreshIdCmd = DeserializePayload<RefreshIdentifiersCommand>(message.Payload);
                await HandleRefreshIdentifiersCommand(refreshIdCmd, appPort);
                break;

            case BwCsMsgTypes.CMD_REFRESH_CREDENTIALS:
                var refreshCredCmd = DeserializePayload<RefreshCredentialsCommand>(message.Payload);
                await HandleRefreshCredentialsCommand(refreshCredCmd, appPort);
                break;

            case BwCsMsgTypes.CMD_CREATE_AID:
                var createAidCmd = DeserializePayload<CreateAidCommand>(message.Payload);
                await HandleCreateAidCommand(createAidCmd, appPort);
                break;

            // Existing handlers
            case BwCsMsgTypes.REPLY:
                await HandleReplyMessageAsync(message, csConnection);
                break;

            // ... other existing cases ...

            default:
                _logger.LogWarning("Unknown message type from Blazor App: {Type}", message.Type);
                break;
        }
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Error handling message from Blazor App");
    }
}
```

---

## Phase 5: App-Side Reactive Services

### 5.1 Reactive Storage Service

**New file: `Extension/Services/ReactiveStorageService/IReactiveStorageService.cs`**

```csharp
namespace Extension.Services.ReactiveStorageService;

public interface IReactiveStorageService {
    IObservable<KeriaConnectionState> KeriaConnectionState { get; }
    IObservable<IdentifiersData> Identifiers { get; }
    IObservable<CredentialsData> Credentials { get; }

    Task Initialize();
}
```

**New file: `Extension/Services/ReactiveStorageService/ReactiveStorageService.cs`**

```csharp
using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.JSInterop;
using WebExtensions.Net;

namespace Extension.Services.ReactiveStorageService;

public class ReactiveStorageService : IReactiveStorageService, IDisposable {
    private readonly ILogger<ReactiveStorageService> _logger;
    private readonly IWebExtensionsApi _webExtensionsApi;
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<ReactiveStorageService>? _dotNetRef;

    private readonly Subject<KeriaConnectionState> _connectionStateSubject = new();
    private readonly Subject<IdentifiersData> _identifiersSubject = new();
    private readonly Subject<CredentialsData> _credentialsSubject = new();

    public IObservable<KeriaConnectionState> KeriaConnectionState => _connectionStateSubject.AsObservable();
    public IObservable<IdentifiersData> Identifiers => _identifiersSubject.AsObservable();
    public IObservable<CredentialsData> Credentials => _credentialsSubject.AsObservable();

    public ReactiveStorageService(
        ILogger<ReactiveStorageService> logger,
        IWebExtensionsApi webExtensionsApi,
        IJSRuntime jsRuntime) {
        _logger = logger;
        _webExtensionsApi = webExtensionsApi;
        _jsRuntime = jsRuntime;
    }

    public async Task Initialize() {
        try {
            _logger.LogInformation("ReactiveStorageService: Initializing session storage listener");

            // Set up session storage listener
            var storageModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./scripts/es6/storageHelper.js");

            _dotNetRef = DotNetObjectReference.Create(this);
            await storageModule.InvokeVoidAsync("addSessionStorageChangeListener", _dotNetRef);

            // Load initial values from session storage
            await LoadInitialConnectionState();
            await LoadInitialIdentifiers();
            await LoadInitialCredentials();

            _logger.LogInformation("ReactiveStorageService: Initialization complete");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ReactiveStorageService: Error during initialization");
        }
    }

    [JSInvokable]
    public async Task NotifySessionStorageChanged(Dictionary<string, Dictionary<string, JsonElement>> changes) {
        _logger.LogInformation("ReactiveStorageService: Session storage changed: {Keys}",
            string.Join(", ", changes.Keys));

        foreach (var (key, change) in changes) {
            try {
                switch (key) {
                    case "keriaConnectionState":
                        if (change.TryGetValue("newValue", out var connStateJson)) {
                            var state = JsonSerializer.Deserialize<KeriaConnectionState>(
                                connStateJson.GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                            if (state != null) {
                                _logger.LogInformation("ReactiveStorageService: Connection state updated: {Status}", state.Status);
                                _connectionStateSubject.OnNext(state);
                            }
                        }
                        break;

                    case "identifiers":
                        if (change.TryGetValue("newValue", out var idJson)) {
                            var data = JsonSerializer.Deserialize<IdentifiersData>(
                                idJson.GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                            if (data != null) {
                                _logger.LogInformation("ReactiveStorageService: Identifiers updated: {Count} AIDs",
                                    data.Aids?.Count ?? 0);
                                _identifiersSubject.OnNext(data);
                            }
                        }
                        break;

                    case "credentials":
                        if (change.TryGetValue("newValue", out var credJson)) {
                            var data = JsonSerializer.Deserialize<CredentialsData>(
                                credJson.GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );
                            if (data != null) {
                                _logger.LogInformation("ReactiveStorageService: Credentials updated: {Count} ACDCs",
                                    data.Acdc?.Count ?? 0);
                                _credentialsSubject.OnNext(data);
                            }
                        }
                        break;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "ReactiveStorageService: Error processing storage change for key: {Key}", key);
            }
        }

        await Task.CompletedTask;
    }

    private async Task LoadInitialConnectionState() {
        try {
            var result = await _webExtensionsApi.Storage.Session.Get("keriaConnectionState");
            if (result != null && result.TryGetValue("keriaConnectionState", out var value)) {
                var state = JsonSerializer.Deserialize<KeriaConnectionState>(
                    value.ToString()!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (state != null) {
                    _logger.LogInformation("ReactiveStorageService: Initial connection state loaded: {Status}", state.Status);
                    _connectionStateSubject.OnNext(state);
                }
            }
            else {
                // No connection state in storage - emit disconnected state
                _connectionStateSubject.OnNext(new KeriaConnectionState("disconnected", 0, null));
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ReactiveStorageService: Error loading initial connection state");
        }
    }

    private async Task LoadInitialIdentifiers() {
        try {
            var result = await _webExtensionsApi.Storage.Session.Get("identifiers");
            if (result != null && result.TryGetValue("identifiers", out var value)) {
                var data = JsonSerializer.Deserialize<IdentifiersData>(
                    value.ToString()!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (data != null) {
                    _logger.LogInformation("ReactiveStorageService: Initial identifiers loaded: {Count} AIDs",
                        data.Aids?.Count ?? 0);
                    _identifiersSubject.OnNext(data);
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ReactiveStorageService: Error loading initial identifiers");
        }
    }

    private async Task LoadInitialCredentials() {
        try {
            var result = await _webExtensionsApi.Storage.Session.Get("credentials");
            if (result != null && result.TryGetValue("credentials", out var value)) {
                var data = JsonSerializer.Deserialize<CredentialsData>(
                    value.ToString()!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (data != null) {
                    _logger.LogInformation("ReactiveStorageService: Initial credentials loaded: {Count} ACDCs",
                        data.Acdc?.Count ?? 0);
                    _credentialsSubject.OnNext(data);
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "ReactiveStorageService: Error loading initial credentials");
        }
    }

    public void Dispose() {
        _dotNetRef?.Dispose();
        _connectionStateSubject?.Dispose();
        _identifiersSubject?.Dispose();
        _credentialsSubject?.Dispose();
    }
}
```

**New file: `Extension/Services/ReactiveStorageService/Models.cs`**

```csharp
using Extension.Services.SignifyService.Models;

namespace Extension.Services.ReactiveStorageService;

// Data models for reactive storage
public record KeriaConnectionState(string Status, long Timestamp, string? Error);

public record IdentifiersData(List<Aid>? Aids, long Timestamp, bool Stale);

public record CredentialsData(List<Credential>? Acdc, long Timestamp, bool Stale);
```

### 5.2 Command Service

**New file: `Extension/Services/BackgroundWorkerCommandService/IBackgroundWorkerCommandService.cs`**

```csharp
using FluentResults;

namespace Extension.Services.BackgroundWorkerCommandService;

public interface IBackgroundWorkerCommandService {
    Task<Result> ConnectToKeria(string url, string passcode, string? bootUrl, bool isBootForced = true);
    Task<Result> DisconnectFromKeria();
    Task<Result> RefreshIdentifiers();
    Task<Result> RefreshCredentials(string? aidName = null, string? issuer = null);
    Task<Result> CreateAid(string name, string? algo = null, int? count = null);
}
```

**New file: `Extension/Services/BackgroundWorkerCommandService/BackgroundWorkerCommandService.cs`**

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Extension.Models.BwCommands;
using Extension.Models.ExCsMessages;
using FluentResults;

namespace Extension.Services.BackgroundWorkerCommandService;

public class BackgroundWorkerCommandService : IBackgroundWorkerCommandService, IDisposable {
    private readonly ILogger<BackgroundWorkerCommandService> _logger;
    private readonly IAppSwMessagingService _messaging;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Result>> _pendingCommands = new();

    public BackgroundWorkerCommandService(
        ILogger<BackgroundWorkerCommandService> logger,
        IAppSwMessagingService messaging) {
        _logger = logger;
        _messaging = messaging;

        _messaging.OnMessageReceived += HandleCommandResponse;
    }

    public async Task<Result> ConnectToKeria(string url, string passcode, string? bootUrl, bool isBootForced = true) {
        var commandId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<Result>();
        _pendingCommands[commandId] = tcs;

        try {
            var command = new ConnectToKeriaCommand(commandId, url, passcode, bootUrl, isBootForced);

            _logger.LogInformation("Sending ConnectToKeria command: {CommandId}", commandId);

            await _messaging.SendMessageToBackgroundWorker(new BwCsMsg(
                type: BwCsMsgTypes.CMD_CONNECT_KERIA,
                requestId: commandId,
                payload: command
            ));

            // Wait for response with timeout
            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
            _pendingCommands.TryRemove(commandId, out _);

            return result;
        }
        catch (TimeoutException) {
            _pendingCommands.TryRemove(commandId, out _);
            return Result.Fail("Command timeout: ConnectToKeria");
        }
        catch (Exception ex) {
            _pendingCommands.TryRemove(commandId, out _);
            _logger.LogError(ex, "Error sending ConnectToKeria command");
            return Result.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<Result> DisconnectFromKeria() {
        var commandId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<Result>();
        _pendingCommands[commandId] = tcs;

        try {
            var command = new DisconnectFromKeriaCommand(commandId);

            _logger.LogInformation("Sending DisconnectFromKeria command: {CommandId}", commandId);

            await _messaging.SendMessageToBackgroundWorker(new BwCsMsg(
                type: BwCsMsgTypes.CMD_DISCONNECT_KERIA,
                requestId: commandId,
                payload: command
            ));

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _pendingCommands.TryRemove(commandId, out _);

            return result;
        }
        catch (TimeoutException) {
            _pendingCommands.TryRemove(commandId, out _);
            return Result.Fail("Command timeout: DisconnectFromKeria");
        }
        catch (Exception ex) {
            _pendingCommands.TryRemove(commandId, out _);
            _logger.LogError(ex, "Error sending DisconnectFromKeria command");
            return Result.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<Result> RefreshIdentifiers() {
        var commandId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<Result>();
        _pendingCommands[commandId] = tcs;

        try {
            var command = new RefreshIdentifiersCommand(commandId);

            _logger.LogInformation("Sending RefreshIdentifiers command: {CommandId}", commandId);

            await _messaging.SendMessageToBackgroundWorker(new BwCsMsg(
                type: BwCsMsgTypes.CMD_REFRESH_IDENTIFIERS,
                requestId: commandId,
                payload: command
            ));

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _pendingCommands.TryRemove(commandId, out _);

            return result;
        }
        catch (TimeoutException) {
            _pendingCommands.TryRemove(commandId, out _);
            return Result.Fail("Command timeout: RefreshIdentifiers");
        }
        catch (Exception ex) {
            _pendingCommands.TryRemove(commandId, out _);
            _logger.LogError(ex, "Error sending RefreshIdentifiers command");
            return Result.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<Result> RefreshCredentials(string? aidName = null, string? issuer = null) {
        var commandId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<Result>();
        _pendingCommands[commandId] = tcs;

        try {
            var command = new RefreshCredentialsCommand(commandId, aidName, issuer);

            _logger.LogInformation("Sending RefreshCredentials command: {CommandId}", commandId);

            await _messaging.SendMessageToBackgroundWorker(new BwCsMsg(
                type: BwCsMsgTypes.CMD_REFRESH_CREDENTIALS,
                requestId: commandId,
                payload: command
            ));

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _pendingCommands.TryRemove(commandId, out _);

            return result;
        }
        catch (TimeoutException) {
            _pendingCommands.TryRemove(commandId, out _);
            return Result.Fail("Command timeout: RefreshCredentials");
        }
        catch (Exception ex) {
            _pendingCommands.TryRemove(commandId, out _);
            _logger.LogError(ex, "Error sending RefreshCredentials command");
            return Result.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<Result> CreateAid(string name, string? algo = null, int? count = null) {
        var commandId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<Result>();
        _pendingCommands[commandId] = tcs;

        try {
            var command = new CreateAidCommand(commandId, name, algo, count);

            _logger.LogInformation("Sending CreateAid command: {CommandId}, Name={Name}", commandId, name);

            await _messaging.SendMessageToBackgroundWorker(new BwCsMsg(
                type: BwCsMsgTypes.CMD_CREATE_AID,
                requestId: commandId,
                payload: command
            ));

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
            _pendingCommands.TryRemove(commandId, out _);

            return result;
        }
        catch (TimeoutException) {
            _pendingCommands.TryRemove(commandId, out _);
            return Result.Fail("Command timeout: CreateAid");
        }
        catch (Exception ex) {
            _pendingCommands.TryRemove(commandId, out _);
            _logger.LogError(ex, "Error sending CreateAid command");
            return Result.Fail($"Error: {ex.Message}");
        }
    }

    private void HandleCommandResponse(object? sender, BwCsMsg msg) {
        if (msg.Type == BwCsMsgTypes.CMD_RESPONSE) {
            try {
                if (_pendingCommands.TryGetValue(msg.RequestId ?? "", out var tcs)) {
                    var responseJson = JsonSerializer.Serialize(msg.Payload);
                    var response = JsonSerializer.Deserialize<BwCommandResponse>(
                        responseJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (response != null) {
                        if (response.Success) {
                            _logger.LogInformation("Command succeeded: {CommandId}", response.CommandId);
                            tcs.SetResult(Result.Ok());
                        } else {
                            _logger.LogWarning("Command failed: {CommandId}, Error={Error}",
                                response.CommandId, response.Error);
                            tcs.SetResult(Result.Fail(response.Error ?? "Unknown error"));
                        }
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error handling command response");
            }
        }
    }

    public void Dispose() {
        _messaging.OnMessageReceived -= HandleCommandResponse;
    }
}
```

---

## Phase 6: Update StorageHelper for Session Storage

**Update `Extension/wwwroot/scripts/es6/storageHelper.ts`:**

```typescript
/// <reference types="chrome-types" />

// Existing local storage listener
export const addStorageChangeListener = (dotNetObject: any): void => {
    chrome.storage.onChanged.addListener(async (changes, area) => {
        if (area === 'local') {
            // Convert changes to a plain object for serialization
            const changesObj: { [key: string]: { oldValue: any, newValue: any } } = {};
            for (const [key, { oldValue, newValue }] of Object.entries(changes)) {
                changesObj[key] = { oldValue, newValue };
            }
            await dotNetObject.invokeMethodAsync('NotifyStorageChanged', changesObj, 'local');
        }
    });
};

// NEW: Session storage listener
export const addSessionStorageChangeListener = (dotNetObject: any): void => {
    chrome.storage.onChanged.addListener(async (changes, area) => {
        if (area === 'session') {
            // Convert changes to a plain object for serialization
            const changesObj: { [key: string]: { oldValue: any, newValue: any } } = {};
            for (const [key, { oldValue, newValue }] of Object.entries(changes)) {
                changesObj[key] = { oldValue, newValue };
            }
            await dotNetObject.invokeMethodAsync('NotifySessionStorageChanged', changesObj);
        }
    });
};
```

**Rebuild TypeScript:**
```bash
cd Extension && npm run build:es6
```

---

## Phase 7: Page Migration Pattern

### 7.1 Example: UnlockPage.razor

**OLD Pattern (Direct SignifyClientService):**
```razor
@page "/unlock"
@inject ISignifyClientService signifyClientService
@inject NavigationManager navigationManager

@code {
    private string? errorMessage;

    private async Task UnlockAsync() {
        var connectRes = await signifyClientService.Connect(
            keriaConnectConfig.AdminUrl,
            password,
            keriaConnectConfig.BootUrl,
            false
        );

        if (connectRes.IsSuccess) {
            navigationManager.NavigateTo("/dashboard");
        } else {
            errorMessage = connectRes.Errors[0].Message;
        }
    }
}
```

**NEW Pattern (Command + Reactive):**
```razor
@page "/unlock"
@inject IBackgroundWorkerCommandService commandService
@inject IReactiveStorageService reactiveStorage
@inject IStateService stateService
@inject NavigationManager navigationManager
@implements IDisposable

@code {
    private string? errorMessage;
    private string? statusMessage;
    private IDisposable? _connectionStateSubscription;

    protected override void OnInitialized() {
        // Subscribe to connection state changes
        _connectionStateSubscription = reactiveStorage.KeriaConnectionState.Subscribe(state => {
            InvokeAsync(() => {
                switch (state.Status) {
                    case "connecting":
                        statusMessage = "Connecting to KERIA...";
                        errorMessage = null;
                        break;

                    case "connected":
                        statusMessage = "Connected!";
                        errorMessage = null;
                        // Update state service and navigate
                        stateService.Authenticate(true);
                        navigationManager.NavigateTo("/dashboard");
                        break;

                    case "error":
                        statusMessage = null;
                        errorMessage = state.Error ?? "Connection failed";
                        break;

                    case "disconnected":
                        statusMessage = null;
                        break;
                }
                StateHasChanged();
            });
        });
    }

    private async Task UnlockAsync() {
        // Clear previous errors
        errorMessage = null;
        statusMessage = "Sending unlock command...";

        // Send command to BackgroundWorker
        var result = await commandService.ConnectToKeria(
            keriaConnectConfig.AdminUrl,
            password,
            keriaConnectConfig.BootUrl,
            false
        );

        // Result tells us if command was accepted, but actual state comes from storage subscription
        if (result.IsFailed) {
            errorMessage = "Failed to send unlock command: " + result.Errors[0].Message;
            statusMessage = null;
        }
    }

    public void Dispose() {
        _connectionStateSubscription?.Dispose();
    }
}
```

### 7.2 Example: IdentifiersPage.razor

**OLD Pattern:**
```razor
@page "/identifiers"
@inject ISignifyClientService signifyClientService

@code {
    private List<Aid>? aids;
    private bool isLoading;

    protected override async Task OnInitializedAsync() {
        isLoading = true;
        var result = await signifyClientService.GetIdentifiers();
        if (result.IsSuccess) {
            aids = result.Value.Aids;
        }
        isLoading = false;
    }
}
```

**NEW Pattern:**
```razor
@page "/identifiers"
@inject IReactiveStorageService reactiveStorage
@inject IBackgroundWorkerCommandService commandService
@implements IDisposable

@code {
    private List<Aid>? aids;
    private bool isStale;
    private IDisposable? _identifiersSubscription;

    protected override void OnInitialized() {
        // Reactively subscribe to identifiers
        _identifiersSubscription = reactiveStorage.Identifiers.Subscribe(data => {
            InvokeAsync(() => {
                aids = data.Aids;
                isStale = data.Stale;
                StateHasChanged();
            });
        });

        // Trigger refresh in background (fire-and-forget)
        // UI will update automatically via subscription when data arrives
        Task.Run(async () => await commandService.RefreshIdentifiers());
    }

    private async Task RefreshAsync() {
        // Manual refresh triggered by user
        await commandService.RefreshIdentifiers();
        // UI will update automatically via subscription
    }

    public void Dispose() {
        _identifiersSubscription?.Dispose();
    }
}
```

### 7.3 Example: CredentialsPage.razor

**NEW Pattern:**
```razor
@page "/credentials"
@inject IReactiveStorageService reactiveStorage
@inject IBackgroundWorkerCommandService commandService
@implements IDisposable

@code {
    private List<Credential>? credentials;
    private bool isStale;
    private IDisposable? _credentialsSubscription;

    protected override void OnInitialized() {
        _credentialsSubscription = reactiveStorage.Credentials.Subscribe(data => {
            InvokeAsync(() => {
                credentials = data.Acdc;
                isStale = data.Stale;
                StateHasChanged();
            });
        });

        // Trigger refresh
        Task.Run(async () => await commandService.RefreshCredentials());
    }

    private async Task RefreshAsync() {
        await commandService.RefreshCredentials();
    }

    public void Dispose() {
        _credentialsSubscription?.Dispose();
    }
}
```

---

## Phase 8: Service Registration

### 8.1 Update Program.cs

**`Extension/Program.cs`:**

```csharp
// After builder initialization and mode detection

if (mode == BrowserExtensionMode.Standard || mode == BrowserExtensionMode.Debug) {
    // ============= APP CONTEXT SERVICES =============

    // NEW: Command and reactive services (replaces direct SignifyClient access)
    builder.Services.AddSingleton<IBackgroundWorkerCommandService, BackgroundWorkerCommandService>();
    builder.Services.AddSingleton<IReactiveStorageService, ReactiveStorageService>();

    // REMOVE: Direct SignifyClient access (moved to BackgroundWorker only)
    // builder.Services.AddSingleton<ISignifyClientService, SignifyClientService>();
    // builder.Services.AddSingleton<ISignifyClientBinding, SignifyClientBinding>();

    // KEEP: Existing App services
    builder.Services.AddSingleton<IStateService, StateService>();
    builder.Services.AddSingleton<IStorageService, StorageService>();
    builder.Services.AddSingleton<IWebsiteConfigService, WebsiteConfigService>();
    // ... other App services
}
else if (mode == BrowserExtensionMode.Background) {
    // ============= BACKGROUNDWORKER CONTEXT SERVICES =============

    // KEEP: SignifyClient only in BackgroundWorker context
    builder.Services.AddSingleton<ISignifyClientService, SignifyClientService>();
    builder.Services.AddSingleton<ISignifyClientBinding, SignifyClientBinding>();

    // KEEP: BackgroundWorker services
    builder.Services.AddSingleton<IStorageService, StorageService>();
    builder.Services.AddSingleton<IWebsiteConfigService, WebsiteConfigService>();
    // ... other BackgroundWorker services
}
```

### 8.2 Initialize Reactive Storage

**In `Extension/App.razor` or main app initialization:**

```csharp
@inject IReactiveStorageService reactiveStorage

protected override async Task OnInitializedAsync() {
    // Initialize reactive storage listener
    await reactiveStorage.Initialize();

    // ... rest of initialization
}
```

---

## Phase 9: BackgroundWorker Lifecycle Management

### 9.1 Auto-Reconnect on Startup

**Update `Extension/BackgroundWorker.cs`:**

```csharp
[JSInvokable]
public async Task OnStartupAsync() {
    try {
        _logger.LogInformation("OnStartupAsync event handler called");
        _logger.LogInformation("Browser startup detected - reinitializing background worker");

        // Check for stored connection config and cached passcode
        var configResult = await _storageService.GetItem<KeriaConnectConfig>();

        if (configResult.IsSuccess && configResult.Value != null) {
            var config = configResult.Value;

            // Check if passcode is still cached in session storage
            var passcodeData = await WebExtensions.Storage.Session.Get("passcode");

            if (passcodeData != null && passcodeData.TryGetValue("passcode", out var passcodeValue)) {
                var passcode = passcodeValue.ToString();

                if (!string.IsNullOrEmpty(passcode)) {
                    _logger.LogInformation("BW Auto-reconnecting to KERIA on startup");

                    // Reconnect in background (non-blocking)
                    _ = Task.Run(async () => {
                        try {
                            await UpdateKeriaConnectionState("connecting", null);

                            var result = await _signifyService.Connect(
                                config.AdminUrl!,
                                passcode,
                                config.BootUrl,
                                false  // Don't force boot on reconnect
                            );

                            if (result.IsSuccess) {
                                _logger.LogInformation("BW Auto-reconnect successful");
                                await UpdateKeriaConnectionState("connected", null);
                                await RefreshIdentifiersToStorage();
                            } else {
                                _logger.LogWarning("BW Auto-reconnect failed: {Error}", result.Errors[0].Message);
                                await UpdateKeriaConnectionState("error", result.Errors[0].Message);
                            }
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "BW Error during auto-reconnect");
                            await UpdateKeriaConnectionState("error", ex.Message);
                        }
                    });
                }
            }
        }

        _logger.LogInformation("Background worker reinitialized on browser startup");
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Error handling onStartup event");
    }
}
```

### 9.2 Handle Sign Requests with Auto-Connect

**Update `Extension/BackgroundWorker.cs` → `HandleSignRequestAsync`:**

```csharp
private async Task HandleSignRequestAsync(CsBwMsg msg, WebExtensions.Net.Runtime.Port csTabPort) {
    try {
        _logger.LogInformation("BW HandleSignRequest: {Message}", JsonSerializer.Serialize(msg));

        // Check if connected to KERIA
        var connectionStateData = await WebExtensions.Storage.Session.Get("keriaConnectionState");

        if (connectionStateData == null ||
            !connectionStateData.TryGetValue("keriaConnectionState", out var connStateValue)) {
            // No connection state - need to connect first
            var errorMsg = new BwCsMsg(
                type: BwCsMsgTypes.REPLY,
                requestId: msg.RequestId,
                error: "Not connected to KERIA. Please unlock first."
            );
            csTabPort.PostMessage(errorMsg);
            return;
        }

        var connState = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            connStateValue.ToString()!
        );

        if (connState == null ||
            !connState.TryGetValue("status", out var statusElement) ||
            statusElement.GetString() != "connected") {
            var errorMsg = new BwCsMsg(
                type: BwCsMsgTypes.REPLY,
                requestId: msg.RequestId,
                error: "Not connected to KERIA. Please unlock first."
            );
            csTabPort.PostMessage(errorMsg);
            return;
        }

        // Connected - proceed with sign request
        // ... rest of existing HandleSignRequestAsync logic ...
    }
    catch (Exception ex) {
        _logger.LogError(ex, "Error in HandleSignRequest");
        // ... error handling ...
    }
}
```

---

## Phase 10: Migration Checklist

### 10.1 Pages to Migrate (22 total)

#### Priority 1 - Core Auth & Data Pages
- [ ] [UnlockPage.razor](Extension/Pages/UnlockPage.razor) - Connect command + connection state subscription
- [ ] [ConfigurePage.razor](Extension/Pages/ConfigurePage.razor) - Connect command after configuration
- [ ] [Index.razor](Extension/Pages/Index.razor) - Connection check + auto-reconnect
- [ ] [IdentifiersPage.razor](Extension/Pages/IdentifiersPage.razor) - Identifiers subscription
- [ ] [CredentialsPage.razor](Extension/Pages/CredentialsPage.razor) - Credentials subscription

#### Priority 2 - Identifier Management
- [ ] [IdentifierPage.razor](Extension/Pages/IdentifierPage.razor) - Single identifier view
- [ ] [DashboardPage.razor](Extension/Pages/DashboardPage.razor) - Dashboard with identifiers

#### Priority 3 - Request Handling
- [ ] [RequestSignInPage.razor](Extension/Pages/RequestSignInPage.razor) - Auth requests from websites
- [ ] [RequestSignPage.razor](Extension/Pages/RequestSignPage.razor) - Sign requests from websites

#### Priority 4 - Configuration & Settings
- [ ] [KeriAgentServicePage.razor](Extension/Pages/KeriAgentServicePage.razor) - KERIA config
- [ ] [WebsitePage.razor](Extension/Pages/WebsitePage.razor) - Website config
- [ ] [WebsitesPage.razor](Extension/Pages/WebsitesPage.razor) - Website list
- [ ] [ConnectingPage.razor](Extension/Pages/ConnectingPage.razor) - Connection progress
- [ ] [AddAuthenticatorPage.razor](Extension/Pages/AddAuthenticatorPage.razor) - WebAuthn setup
- [ ] [Authenticators.razor](Extension/Pages/Authenticators.razor) - Authenticator list

#### Priority 5 - Static/Info Pages (minimal changes)
- [ ] [WelcomePage.razor](Extension/Pages/WelcomePage.razor)
- [ ] [PrivacyPage.razor](Extension/Pages/PrivacyPage.razor)
- [ ] [TermsPage.razor](Extension/Pages/TermsPage.razor)
- [ ] [NewReleasePage.razor](Extension/Pages/NewReleasePage.razor)

#### Priority 6 - Components & Layouts
- [ ] [WebsiteConfigDisplay.razor](Extension/Components/WebsiteConfigDisplay.razor) - Component
- [ ] [MainLayout.razor](Extension/Layouts/MainLayout.razor) - Layout
- [ ] [BaseLayout.razor](Extension/Layouts/BaseLayout.razor) - Layout

### 10.2 Code Changes Checklist

#### Phase 4 - BackgroundWorker
- [ ] Create `Extension/Models/BwCommands/BwCommand.cs` and command records
- [ ] Create `Extension/BackgroundWorker.Commands.cs` (partial class)
- [ ] Implement command handlers in BackgroundWorker.Commands.cs
- [ ] Update `BwCsMsgTypes.cs` with new command constants
- [ ] Update message router in `SetUpBlazorAppMessageHandling`

#### Phase 5 - App Services
- [ ] Create `Extension/Services/ReactiveStorageService/` folder
- [ ] Implement `IReactiveStorageService.cs`
- [ ] Implement `ReactiveStorageService.cs`
- [ ] Create `Models.cs` for reactive data types
- [ ] Create `Extension/Services/BackgroundWorkerCommandService/` folder
- [ ] Implement `IBackgroundWorkerCommandService.cs`
- [ ] Implement `BackgroundWorkerCommandService.cs`

#### Phase 6 - Storage Helper
- [ ] Update `Extension/wwwroot/scripts/es6/storageHelper.ts`
- [ ] Add `addSessionStorageChangeListener` function
- [ ] Rebuild TypeScript: `cd Extension && npm run build:es6`

#### Phase 8 - Service Registration
- [ ] Update `Extension/Program.cs` for App context
- [ ] Update `Extension/Program.cs` for BackgroundWorker context
- [ ] Add ReactiveStorageService initialization in App.razor

#### Phase 9 - BackgroundWorker Lifecycle
- [ ] Update `OnStartupAsync` with auto-reconnect logic
- [ ] Update `HandleSignRequestAsync` to check connection state
- [ ] Add connection state check helper methods

### 10.3 Testing Checklist

#### Connection Flow
- [ ] Test: Connect from UnlockPage → Connection state updates in storage
- [ ] Test: Close App → BackgroundWorker maintains connection
- [ ] Test: Reopen App → Connection state loads from storage
- [ ] Test: Browser restart → Auto-reconnect on startup (if passcode cached)

#### Data Synchronization
- [ ] Test: Identifiers update in one tab → Other tabs see changes
- [ ] Test: Create AID → Identifiers list refreshes automatically
- [ ] Test: Refresh credentials → Credentials list updates in UI

#### Sign Request Flow
- [ ] Test: Sign request from website → BackgroundWorker uses existing connection
- [ ] Test: Sign request when disconnected → Proper error message
- [ ] Test: Multiple sign requests in parallel → Handled correctly

#### Error Handling
- [ ] Test: KERIA unreachable → Error state in storage
- [ ] Test: Invalid passcode → Error state in storage
- [ ] Test: Command timeout → Proper error returned

---

## Benefits Summary

### ✅ Architectural Benefits
1. **Single Source of Truth**: BackgroundWorker owns the KERIA connection
2. **Persistent Connection**: Survives App close/reopen
3. **Clean Separation**: UI (App) vs Business Logic (BackgroundWorker)
4. **Type Safety**: FluentResults pattern maintained throughout

### ✅ User Experience Benefits
1. **Multi-Window Sync**: All tabs see same data automatically
2. **Faster UI**: Display cached data immediately, refresh in background
3. **Better Feedback**: Real-time connection state updates
4. **Resilient**: Auto-reconnect on browser restart

### ✅ Developer Experience Benefits
1. **Reactive Pattern**: Pages just subscribe to data, no manual fetching
2. **Simplified Pages**: Less boilerplate, clearer intent
3. **Testability**: Clear boundaries, easy to mock storage/commands
4. **Maintainability**: Centralized business logic in BackgroundWorker

---

## Estimated Implementation Time

- **Phase 4** (BackgroundWorker handlers): 3-4 hours
- **Phase 5** (App services): 3-4 hours
- **Phase 6** (Storage helper): 1 hour
- **Phase 7** (Page migration): 6-8 hours (22 pages)
- **Phase 8** (Service registration): 1 hour
- **Phase 9** (Lifecycle management): 2-3 hours
- **Testing**: 3-4 hours

**Total: 19-25 hours**

---

## Next Steps

1. Start with Phase 4: Create BackgroundWorker command handlers
2. Then Phase 5: Implement App-side reactive services
3. Phase 6: Update storage helper for session storage
4. Phase 7: Migrate pages incrementally (start with Priority 1)
5. Test thoroughly after each phase

---

## Notes & Open Questions

### Open Questions
- [ ] Should we add a "stale data" indicator in the UI?
- [ ] What's the timeout for marking identifiers/credentials as stale?
- [ ] Should we persist identifiers/credentials to local storage as a cache?

### Future Enhancements
- [ ] Add credential issuance commands
- [ ] Add key rotation commands
- [ ] Add OOBI resolution commands
- [ ] Add notification system for background events

---

## References

- [BLAZOR_WASM_STARTUP_FLOWS.md](BLAZOR_WASM_STARTUP_FLOWS.md) - Blazor runtime architecture
- [CLAUDE.md](CLAUDE.md) - Project coding guidelines
- [Extension/BackgroundWorker.cs](Extension/BackgroundWorker.cs) - Current BackgroundWorker implementation
- [Extension/Services/StateService.cs](Extension/Services/StateService.cs) - Existing state management pattern
