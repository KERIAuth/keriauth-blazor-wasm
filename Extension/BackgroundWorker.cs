using Blazor.BrowserExtension;
using Extension.Models;
using Extension.Models.ExCsMessages;
using Extension.Services;
using Extension.Services.SignifyService;
using JsBind.Net;
using Microsoft.JSInterop;
using MudBlazor.Extensions;
using System.Collections.Concurrent;
using System.Text.Json;
using WebExtensions.Net;
using WebExtensions.Net.Manifest;
using WebExtensions.Net.Permissions;

namespace Extension;

/// <summary>
/// Background worker for the browser extension, handling message routing between
/// content scripts, the Blazor app, and KERIA services.
/// </summary>

public partial class BackgroundWorker : BackgroundWorkerBase, IDisposable {

    // Constants
    private const string ContentScriptPortPattern = @"^[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}$";
    private const string BlazorAppPortPrefix = "blazorAppPort";
    private const string UninstallUrl = "https://keriauth.com/uninstall.html";
    private const string DefaultVersion = "unknown";

    // Cached JsonSerializerOptions for port message deserialization
    private static readonly JsonSerializerOptions PortMessageJsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    // Cached JsonSerializerOptions for credential deserialization with increased depth
    // vLEI credentials can have deeply nested structures (edges, rules, chains, etc.)
    private static readonly JsonSerializerOptions CredentialJsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 128  // Increased from default 32 to handle deeply nested vLEI credential structures
    };

    // Install reasons
    private const string InstallReason = "install";
    private const string UpdateReason = "update";
    private const string ChromeUpdateReason = "chrome_update";
    private const string SharedModuleUpdateReason = "shared_module_update";

    // Message types
    private const string LockAppAction = "LockApp";
    private const string SystemLockDetectedAction = "systemLockDetected";
    private const string FromBackgroundWorkerType = "fromBackgroundWorker";

    private readonly ILogger<BackgroundWorker> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly IJsRuntimeAdapter _jsRuntimeAdapter;
    private readonly IStorageService _storageService;
    private readonly ISignifyClientService _signifyService;
    private readonly IWebsiteConfigService _websiteConfigService;
    private readonly WebExtensionsApi _webExtensionsApi;

    // Port connections tracking
    private readonly ConcurrentDictionary<string, CsConnection> _pageCsConnections = new();
    private readonly ConcurrentDictionary<string, BlazorAppConnection> _blazorAppConnections = new();
    private readonly DotNetObjectReference<BackgroundWorker>? _dotNetObjectRef;

    // State tracking for KERIA operations
    private bool _isWaitingOnKeria;
    private string? _pendingRequestId;

    [BackgroundWorkerMain]
    public override void Main() {
        // JavaScript module imports are handled in app.ts afterStarted() hook
        // which is called after Blazor is fully initialized and ready

        // The build-generated backgroundWorker.js invokes the following content as js-equivalents
        WebExtensions.Runtime.OnInstalled.AddListener(OnInstalledAsync);
        WebExtensions.Runtime.OnStartup.AddListener(OnStartupAsync);

        WebExtensions.Runtime.OnConnect.AddListener(OnConnect);


        WebExtensions.Runtime.OnMessage.AddListener(OnMessageAsync);
        WebExtensions.Alarms.OnAlarm.AddListener(OnAlarmAsync);
        // Don't add an OnClicked handler here because it would be invoked after the one registered in app.ts, and may result in race conditions.
        // WebExtensions.Action.OnClicked.AddListener(OnActionClickedAsync);
        WebExtensions.Tabs.OnRemoved.AddListener(OnTabRemovedAsync);
        WebExtensions.Runtime.OnSuspend.AddListener(OnSuspendAsync);
        WebExtensions.Runtime.OnSuspendCanceled.AddListener(OnSuspendCanceledAsync);
        // TODO P2 WebExtensions.WebNavigation.OnCompleted.AddListener(OnWebNavCompletedAsync);
        //   Parameters: details - Object with tabId, frameId, url, processId, timeStamp
        // TODO P2 WebExtensions.WebRequest.OnCompleted.AddListener(OnWebReqCompletedAsync);
        //   Parameters: details - Object with requestId, url, method, frameId, tabId, type, timeStamp, etc.
        // TODO P3 WebExtensions.Runtime.OnMessageExternal.AddListener(OnMessageExternalAsync);
        //   Parameters: message (any), sender (MessageSender), sendResponse (function)
        // TODO P2 chrome.webRequest.onBeforeRequest // (network/page events)
        //   Parameters: details - Object with requestId, url, method, frameId, tabId, type, timeStamp, requestBody
    }

    // DEPRECATED: This method is no longer used - see OnInstalledAsync() instead
    // Kept for reference only
    private async Task OnInstalled() {
        _logger.LogInformation("BW: OnInstalled...");
        var indexPageUrl = WebExtensions.Runtime.GetURL("index.html");

        var tab = await WebExtensions.Tabs.Create(new() {
            Url = indexPageUrl
        });
        if (tab is not null) {
            tab.Active = true;
        }
        else {
            throw new ArgumentException("indexPageUrl");
        }
    }

    public BackgroundWorker(
        ILogger<BackgroundWorker> logger,
        IJSRuntime jsRuntime,
        IJsRuntimeAdapter jsRuntimeAdapter,
        IStorageService storageService,
        ISignifyClientService signifyService,
        IWebsiteConfigService websiteConfigService) {
        _logger = logger;
        _jsRuntime = jsRuntime;
        _jsRuntimeAdapter = jsRuntimeAdapter;
        _storageService = storageService;
        _signifyService = signifyService;
        _websiteConfigService = websiteConfigService;
        _webExtensionsApi = new WebExtensionsApi(_jsRuntimeAdapter);
        _dotNetObjectRef = DotNetObjectReference.Create(this);
    }

    // OnInstalled fires when the extension is first installed, updated, or Chrome is updated. Good for setup tasks (e.g., initialize storage, create default rules).
    // Parameter: details - OnInstalledDetails with reason, previousVersion, and id
    [JSInvokable]
    public async Task OnInstalledAsync(object detailsObj) {
        try {
            _logger.LogInformation("OnInstalledAsync event handler called");
            _logger.LogInformation("Extension installed/updated event received");

            // Deserialize to strongly-typed object
            var detailsJson = JsonSerializer.Serialize(detailsObj);
            var details = JsonSerializer.Deserialize<OnInstalledDetails>(detailsJson);

            if (details == null) {
                _logger.LogWarning("Failed to deserialize installation details");
                return;
            }

            // TODO P3 set a URL that Chrome will open when the extension is uninstalled, to be used for survey or cleanup instructions.
            // await WebExtensions.Runtime.SetUninstallURL(UninstallUrl);
            _ = UninstallUrl;

            _logger.LogInformation("Extension installed/updated: {Reason}", details.Reason);

            switch (details.Reason) {
                case InstallReason:
                    await HandleInstallAsync();
                    break;
                case UpdateReason:
                    await HandleUpdateAsync(details.PreviousVersion ?? DefaultVersion);
                    break;
                case ChromeUpdateReason:
                case SharedModuleUpdateReason:
                default:
                    _logger.LogInformation("Unhandled install reason: {Reason}", details.Reason);
                    break;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onInstalled event");
            throw;
        }
    }

    // onStartup fires when Chrome launches with a profile (not incognito) that has the extension installed
    // Typical use: Re-initialize or restore state, reconnect services, refresh caches.
    // Parameters: none
    [JSInvokable]
    public async Task OnStartupAsync() {
        try {
            _logger.LogInformation("OnStartupAsync event handler called");
            _logger.LogInformation("Browser startup detected - reinitializing background worker");

            // TODO P2 Reinitialize inactivity timer?
            // TODO P2 add a lock icon to the extension icon

            _logger.LogInformation("Background worker reinitialized on browser startup");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onStartup event");
            throw;
        }
    }


    [JSInvokable]
    public Task OnConnect(WebExtensions.Net.Runtime.Port port) {
        _logger.LogInformation("Port connected: name: { Name } url: {url} port: {port}", port.Name, port.Sender?.Url, port.ToString());
        string? connectionId = port.Name ?? Guid.NewGuid().ToString();
        _logger.LogInformation("Port connected: { ConnectionId} ", connectionId);

        // Parse port name to determine the connection type
        var (portType, portGuid) = ParsePortName(connectionId);
        _logger.LogInformation("Port connection type: {portType}, GUID: {portGuid}", portType, portGuid);

        // Set up appropriate message handling based on port type
        switch (portType) {
            case "CS":
                SetUpContentScriptMessageHandling(port, connectionId);
                break;
            case "BA_TAB":
            case "BA_POPUP":
            case "BA_SIDEPANEL":
                SetUpBlazorAppMessageHandling(port, connectionId, portType);
                break;
            default:
                _logger.LogWarning("Unknown port type: {portType} for connection {connectionId}", portType, connectionId);
                SetUpDefaultMessageHandling(port, connectionId);
                break;
        }

        // Clean up on disconnect
        port.OnDisconnect.AddListener(() => {
            _logger.LogInformation("Port disconnected: {ConnectionId}", connectionId);
        });

        return Task.CompletedTask;
    }

    private static (string portType, string portGuid) ParsePortName(string portName) {
        if (string.IsNullOrEmpty(portName)) {
            return ("UNKNOWN", Guid.NewGuid().ToString());
        }

        var parts = portName.Split('|', 2);
        if (parts.Length == 2) {
            return (parts[0], parts[1]);
        }

        // Legacy format or unexpected format - treat as unknown
        return ("LEGACY", portName);
    }

    private void SetUpContentScriptMessageHandling(WebExtensions.Net.Runtime.Port port, string connectionId) {
        _logger.LogInformation("Setting up ContentScript message handling for port {ConnectionId}", connectionId);

        // Store the content script connection
        var tabId = port.Sender?.Tab?.Id ?? -1;
        var origin = port.Sender?.Url ?? "unknown";
        _pageCsConnections[connectionId] = new CsConnection {
            Port = port,
            TabId = tabId,
            PageAuthority = GetAuthorityFromUrl(origin)
        };

        // Clean up on disconnect
        port.OnDisconnect.AddListener(() => {
            _logger.LogInformation("Content Script port disconnected: {ConnectionId}", connectionId);
            _pageCsConnections.TryRemove(connectionId, out _);
        });

        port.OnMessage.AddListener(async (object message, MessageSender sender, Action sendResponse) => {
            _logger.LogInformation("Received ContentScript message on port {ConnectionId}: {Message}", connectionId, message);

            try {
                // Try to deserialize the message as CsBwMsg
                CsBwMsg? csBwMsg = null;

                // Handle different types of incoming message formats
                if (message is JsonElement jsonElement) {
                    // Message came as JsonElement
                    var json = jsonElement.GetRawText();
                    _logger.LogInformation("Message as JsonElement: {json}", json);
                    csBwMsg = JsonSerializer.Deserialize<CsBwMsg>(json, PortMessageJsonOptions);
                }
                else if (message is string jsonString) {
                    // Message came as string
                    _logger.LogInformation("Message as string: {jsonString}", jsonString);
                    csBwMsg = JsonSerializer.Deserialize<CsBwMsg>(jsonString, PortMessageJsonOptions);
                }
                else if (message is Dictionary<string, object> msgDict) {
                    // Message came as Dictionary (legacy path)
                    _logger.LogInformation("Message as Dictionary");
                    var json = JsonSerializer.Serialize(msgDict);
                    csBwMsg = JsonSerializer.Deserialize<CsBwMsg>(json, PortMessageJsonOptions);
                }
                else {
                    // Try to serialize then deserialize to handle other object types
                    var json = JsonSerializer.Serialize(message);
                    _logger.LogInformation("Message serialized from object: {json}", json);
                    csBwMsg = JsonSerializer.Deserialize<CsBwMsg>(json, PortMessageJsonOptions);
                }

                if (csBwMsg != null) {
                    // _logger.LogInformation("Successfully deserialized CsBwMsg with type: {Type}", csBwMsg.Type);

                    _logger.LogInformation("Received {Type} message from ContentScript", csBwMsg.Type);
                    // Handle different message types using the new message type constants
                    switch (csBwMsg.Type) {
                        case CsBwMsgTypes.INIT:
                            // Send READY response using the proper message type
                            var readyMsg = new BwCsMsgPong();
                            port.PostMessage(readyMsg);
                            break;

                        case CsBwMsgTypes.POLARIS_SIGNIFY_AUTHORIZE:
                            await HandleSelectAuthorizeAsync(csBwMsg, port);
                            break;

                        case CsBwMsgTypes.POLARIS_SELECT_AUTHORIZE_CREDENTIAL:
                            _logger.LogWarning("Behavior for message {Type} not yet implemented", csBwMsg.Type);
                            break;

                        case CsBwMsgTypes.POLARIS_SELECT_AUTHORIZE_AID:
                            _logger.LogWarning("Behavior for message {Type} not yet implemented", csBwMsg.Type);
                            break;


                        case CsBwMsgTypes.POLARIS_SIGN_REQUEST:
                            await HandleSignRequestAsync(csBwMsg, port);
                            break;

                        case CsBwMsgTypes.POLARIS_SIGNIFY_EXTENSION_CLIENT:
                            _logger.LogWarning("Behavior for message {Type} not yet implemented", csBwMsg.Type);
                            // TODO: Handle response with extension client info
                            break;

                        default:
                            _logger.LogWarning("Unknown message type on port: {ConnectionId} type: {Type}", connectionId, csBwMsg.Type);
                            var errorMsg = new BwCsMsg(
                                type: BwCsMsgTypes.REPLY,
                                requestId: csBwMsg.RequestId,
                                error: $"Unknown message type: {csBwMsg.Type}"
                            );
                            port.PostMessage(errorMsg);
                            break;
                    }
                }
                else {
                    _logger.LogWarning("Failed to deserialize message from ContentScript as CsBwMsg");
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error processing ContentScript message on port {ConnectionId}", connectionId);
                try {
                    var errorMsg = new BwCsMsg(
                        type: BwCsMsgTypes.REPLY,
                        error: $"Error processing ContentScript message: {ex.Message}"
                    );
                    port.PostMessage(errorMsg);
                }
                catch (Exception sendEx) {
                    _logger.LogError(sendEx, "Failed to send error response to ContentScript on port {ConnectionId}", connectionId);
                }
            }

            return false;
        });
    }

    private void SetUpBlazorAppMessageHandling(WebExtensions.Net.Runtime.Port port, string connectionId, string portType) {
        _logger.LogInformation("Setting up Blazor App ({portType}) message handling for port {ConnectionId}", portType, connectionId);

        // Store the Blazor app connection
        var tabId = port.Sender?.Tab?.Id ?? -1;
        _blazorAppConnections[connectionId] = new BlazorAppConnection {
            Port = port,
            ConnectionId = connectionId,
            TabId = tabId,
            PortType = portType
        };

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

        // Clean up on disconnect
        port.OnDisconnect.AddListener(() => {
            _logger.LogInformation("Blazor App port disconnected: {ConnectionId}", connectionId);
            _blazorAppConnections.TryRemove(connectionId, out _);
        });
    }

    private void SetUpDefaultMessageHandling(WebExtensions.Net.Runtime.Port port, string connectionId) {
        _logger.LogWarning("Setting up default message handling for port {ConnectionId}", connectionId);
        port.OnMessage.AddListener((object message, MessageSender sender, Action sendResponse) => {
            _logger.LogWarning("Received message on legacy/unknown port {ConnectionId}: {Message}", connectionId, message);
            return false;
        });
    }

    /// <summary>
    /// Handles messages from the Blazor App (popup/tab/sidepanel) and forwards them to the appropriate content script
    /// </summary>
    private async Task HandleMessageFromAppAsync(object messageObj, WebExtensions.Net.Runtime.Port appPort, string appConnectionId) {
        try {
            _logger.LogInformation("BW🡠App message, port: {Message}", JsonSerializer.Serialize(messageObj));

            // Deserialize the message
            var messageJson = JsonSerializer.Serialize(messageObj);
            var message = JsonSerializer.Deserialize<BwCsMsg>(messageJson, PortMessageJsonOptions);

            if (message == null) {
                _logger.LogWarning("Failed to deserialize message from Blazor App");
                return;
            }

            // Send acknowledgement back to the app
            var ackMsg = new {
                type = BwCsMsgTypes.FSW,
                data = $"BW received your message: {message.Type} for tab {appPort.Sender?.Tab?.Id}"
            };
            appPort.PostMessage(ackMsg);

            // Find the associated content script connection
            // For now, we'll use the first available CS connection (TODO P2: improve tab matching)
            var csConnection = _pageCsConnections.Values.FirstOrDefault();

            if (csConnection != null) {
                _logger.LogInformation("BW handling App message of type: {Type}", message.Type);

                switch (message.Type) {
                    case BwCsMsgTypes.REPLY:
                        await HandleReplyMessageAsync(message, csConnection);
                        break;

                    case "ApprovedSignRequest":
                        await HandleApprovedSignRequestAsync(message, csConnection);
                        break;

                    case BwCsMsgTypes.REPLY_CRED:
                        await HandleReplyCredentialAsync(message, csConnection);
                        break;

                    case BwCsMsgTypes.REPLY_CANCELED:
                    case "/KeriAuth/signify/replyCancel":
                        await HandleReplyCanceledAsync(message, csConnection);
                        break;

                    case BwCsMsgTypes.APP_CLOSED:
                        await HandleAppClosedAsync(csConnection);
                        break;

                    default:
                        _logger.LogWarning("Unknown message type from Blazor App: {Type}", message.Type);
                        break;
                }
            }
            else {
                _logger.LogWarning("No content script connection found to forward message to");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling message from Blazor App");
        }
    }

    private async Task HandleReplyMessageAsync(BwCsMsg message, CsConnection csConnection) {
        _isWaitingOnKeria = true;
        _logger.LogInformation("BW🡢CS {Type}", message.Type);
        csConnection.Port.PostMessage(message);
        ResetPendingRequest();
        await Task.CompletedTask;
    }

    private async Task HandleApprovedSignRequestAsync(BwCsMsg message, CsConnection csConnection) {
        _isWaitingOnKeria = true;
        await SignRequestSendToTabAsync(message, csConnection);
        ResetPendingRequest();
    }

    private async Task HandleReplyCredentialAsync(BwCsMsg message, CsConnection csConnection) {
        _isWaitingOnKeria = true;
        try {
            if (message.Payload == null) {
                _logger.LogWarning("REPLY_CRED message has null payload");
                return;
            }

            // Extract credential and identifier from payload
            // TODO: Parse credential.raw and get signed headers from SignifyService
            // For now, just forward the message as-is
            _logger.LogInformation("Processing REPLY_CRED - credential signing not yet fully implemented");

            // Calculate expiry (30 minutes from now)
            var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();

            // TODO: Connect to KERIA and get signed headers
            // var isConnected = await _signifyService.IsConnectedAsync();
            // if (isConnected) {
            //     var headers = await _signifyService.GetSignedHeadersAsync(...);
            //     ...
            // }

            // Parse and reconstruct payload with expiry and headers
            var payloadJson = JsonSerializer.Serialize(message.Payload);
            using var jsonDoc = JsonDocument.Parse(payloadJson);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("credential", out var credentialElement)) {
                _logger.LogWarning("REPLY_CRED payload missing 'credential' property");
                return;
            }

            // Extract the raw credential JSON string to avoid deep serialization issues
            // The credential will be parsed on the JavaScript side
            var credentialJson = credentialElement.GetRawText();

            // Create a payload object that includes the credential as a raw JSON string
            // JavaScript will parse this string to get the actual credential object
            var payloadObj = new {
                credentialJson,  // Send as string, JS will parse it
                expiry,
                headers = new Dictionary<string, string>() // TODO: Get from SignifyService
            };

            var authorizeResult = new BwCsMsg(
                type: BwCsMsgTypes.REPLY,
                requestId: message.RequestId,
                payload: payloadObj
            );

            _logger.LogInformation("BW🡢CS authorizeResult");
            csConnection.Port.PostMessage(authorizeResult);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing REPLY_CRED message");
        }

        ResetPendingRequest();
        await Task.CompletedTask;
    }

    private async Task HandleReplyCanceledAsync(BwCsMsg message, CsConnection csConnection) {
        _isWaitingOnKeria = true;
        try {
            var cancelResult = new CsPageMsgData<object?>(
                type: BwCsMsgTypes.REPLY_CANCELED,
                requestId: message.RequestId ?? "unknown",
                source: CsPageConstants.CsPageMsgTag,
                payload: null,
                error: "Canceled or timed out"
            );

            _logger.LogInformation("BW🡢CS cancelResult");
            csConnection.Port.PostMessage(cancelResult);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing REPLY_CANCELED message");
        }

        ResetPendingRequest();
        await Task.CompletedTask;
    }

    private async Task HandleAppClosedAsync(CsConnection csConnection) {
        try {
            if (_pendingRequestId != null) {
                if (!_isWaitingOnKeria) {
                    var cancelResult = new CsPageMsgData<object?>(
                        type: BwCsMsgTypes.REPLY_CANCELED,
                        requestId: _pendingRequestId,
                        source: CsPageConstants.CsPageMsgTag,
                        payload: null,
                        error: "User canceled or KERI Auth timed out"
                    );

                    _logger.LogInformation("BW🡢CS cancelResult (app closed)");
                    csConnection.Port.PostMessage(cancelResult);
                }
                else {
                    _logger.LogInformation("BW not sending cancel to CS to allow Signify signing to complete");
                }
            }
            else {
                var closeAppMsg = new CsPageMsgData<object?>(
                    type: BwCsMsgTypes.APP_CLOSED,
                    requestId: "none",
                    source: CsPageConstants.CsPageMsgTag,
                    payload: null,
                    error: "KERI Auth action popup closed"
                );

                _logger.LogInformation("BW🡢CS closeAppMsg");
                csConnection.Port.PostMessage(closeAppMsg);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error processing APP_CLOSED message");
        }

        await Task.CompletedTask;
    }

    private async Task SignRequestSendToTabAsync(BwCsMsg message, CsConnection csConnection) {
        try {
            // TODO: Implement sign request logic
            // This should process the approved sign request and send it to the content script
            _logger.LogInformation("SignRequestSendToTab not yet fully implemented");
            csConnection.Port.PostMessage(message);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in SignRequestSendToTab");
        }

        await Task.CompletedTask;
    }

    private void ResetPendingRequest() {
        _isWaitingOnKeria = false;
        _pendingRequestId = null;
    }


    // onConnect fires when a connection is made from content scripts or other extension pages
    // Parameter: port - Port object with name and sender properties
    [JSInvokable]
    public async Task OnConnectAsync(object portObjRaw) {
        try {
            _logger.LogInformation("OnConnectAsync event handler called");

            /*
            // Deserialize to strongly-typed object
            var portJson = JsonSerializer.Serialize(portObj);
            var port = JsonSerializer.Deserialize<Extension.Models.Port>(portJson);

            
            
            
            if (port == null) {
                _logger.LogWarning("Failed to deserialize port connection");
                return;
            }

            var connectionId = port.Name ?? Guid.NewGuid().ToString();
            _logger.LogInformation("Port connected: {Name}", connectionId);

            var tabId = port.Sender?.Tab?.Id ?? -1;
            var origin = port.Sender?.Origin ?? port.Sender?.Url ?? "unknown";
            

            // Check if this is a content script port (UUID pattern)
            if (Regex.IsMatch(connectionId, ContentScriptPortPattern)) {
            */
            _logger.LogInformation("Port connected as Regex");

            // Attempt to cast to WebExtensions.Net.Runtime.Port
            var portObj = portObjRaw.As<WebExtensions.Net.Runtime.Port>();
            _logger.LogWarning("Port object received: {PortObjRaw}", portObjRaw);

            // var portObj = portObjRaw as WebExtensions.Net.Runtime.Port;
            if (portObj == null) {
                _logger.LogWarning("Failed to cast port object {PortObjRaw} to WebExtensions.Net.Runtime.Port", JsonSerializer.Serialize(portObjRaw));
                return;
            }


            // EE TMP
            // var port2obj = portObj as WebExtensions.Net.Runtime.Port;
            portObj!.PostMessage(new { foo = "hello from BW" });

            portObj!.OnMessage.AddListener((object message, MessageSender sender, Action<object> sendResponse, bool b) => {
                _logger.LogInformation("Port2obj message received: message {Message} sender {Sender} action {Action}, bool {B}", JsonSerializer.Serialize(message), JsonSerializer.Serialize(sender), sendResponse.ToString(), b.ToString());
                return true;
            });





            // await HandleContentScriptConnectionAsync(connectionId, portObj, port, tabId);
            /*
        }
        else if (connectionId.StartsWith(BlazorAppPortPrefix, StringComparison.OrdinalIgnoreCase)) {
            _logger.LogInformation("Port connected as AppPort");
            await HandleBlazorAppConnectionAsync(connectionId, portObj, port, tabId, origin);
        }
        else {
            _logger.LogWarning("Unknown port connection: {ConnectionId}", connectionId);
        }
        */
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port connection");
        }
    }


    // OnMessage fires when extension parts or external apps send messages.
    // Typical use: Coordination, data requests.
    // Returns: Response to send back to the message sender (or null)
    [JSInvokable]
    public async Task<object?> OnMessageAsync(object messageObj, object senderObj) {
        try {
            _logger.LogInformation("OnMessageAsync event handler called");

            // Deserialize sender to strongly-typed object
            var senderJson = JsonSerializer.Serialize(senderObj);
            var sender = JsonSerializer.Deserialize<Extension.Models.MessageSender>(senderJson);

            // Deserialize message as RuntimeMessage
            var messageJson = JsonSerializer.Serialize(messageObj);
            var message = JsonSerializer.Deserialize<RuntimeMessage>(messageJson);

            if (message?.Action != null) {
                _logger.LogInformation("Runtime message received: {Action} from {SenderId}", message.Action, sender?.Id);

                return message.Action switch {
                    // "resetInactivityTimer" => await ResetInactivityTimerAsync(),
                    LockAppAction => await HandleLockAppMessageAsync(),
                    SystemLockDetectedAction => await HandleSystemLockDetectedAsync(),
                    _ => await HandleUnknownMessageAsync(message.Action)
                };
            }

            return null;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling runtime message");
            return null;
        }
    }

    // onAlarm fires at a scheduled interval/time.
    // Typical use: Periodic tasks, background sync
    [JSInvokable]
    public async Task OnAlarmAsync(object alarmObj) {
        try {
            _logger.LogInformation("OnAlarmAsync event handler called");
            _logger.LogInformation("LIFECYCLE: Background worker reactivated by alarm event at {Timestamp}", DateTime.UtcNow);

            // Deserialize to strongly-typed object
            var alarmJson = JsonSerializer.Serialize(alarmObj);
            var alarm = JsonSerializer.Deserialize<Alarm>(alarmJson);

            if (alarm != null) {
                _logger.LogInformation("SECURITY: Alarm '{AlarmName}' fired - processing security action", alarm.Name);

                // Convert scheduledTime from milliseconds to DateTime if needed
                var scheduledDateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)alarm.ScheduledTime).UtcDateTime;
                _logger.LogInformation("Alarm scheduled for {ScheduledTime}, period: {Period} minutes",
                    scheduledDateTime, alarm.PeriodInMinutes ?? 0);

                // The InactivityTimerService handles the actual alarm logic through its own listener
                _logger.LogInformation("Alarm event will be processed by InactivityTimerService");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling alarm");
        }
    }

    // NOTE: Action click permission handling is now done in app.js beforeStart() hook to preserve user gesture.
    // See app.js for chrome.action.onClicked listener that handles permission requests and script registration.
    //
    // This OnActionClickedAsync method will be invoked after the handler above, since this one is registered after it.
    // Typical use: Open popup, toggle feature, 
    [JSInvokable]
    public async Task OnActionClickedAsync(object tabObj) {
        try {
            _logger.LogInformation("OnActionClickedAsync event handler called");

            // TODO P2: Remove most of this?

            // Deserialize to strongly-typed object
            var tabJson = JsonSerializer.Serialize(tabObj);
            var tab = JsonSerializer.Deserialize<Tab>(tabJson);

            if (tab == null || tab.Id <= 0 || string.IsNullOrEmpty(tab.Url)) {
                _logger.LogWarning("Invalid tab information");
                return;
            }

            _logger.LogInformation("Action button clicked on tab: {TabId}, URL: {Url}", tab.Id, tab.Url);
            // NOTE: Actual permission request and script registration is now handled in app.js

            // 1) Compute per-origin match patterns from the clicked tab
            var matchPatterns = BuildMatchPatternsFromTabUrl(tab.Url);
            if (matchPatterns.Count == 0) {
                _logger.LogInformation("KERIAuth BW: Unsupported or restricted URL scheme; not registering persistence. URL: {Url}", tab.Url);
                return;
            }

            // Check if permissions are already granted.
            var anyPermissions = new AnyPermissions {
                Origins = [.. matchPatterns.Select(pattern => new MatchPattern(new MatchPatternRestricted(pattern)))]
            };
            bool granted = false;
            try {
                granted = await WebExtensions.Permissions.Contains(anyPermissions);
                if (granted) {
                    _logger.LogInformation("KERIAuth BW: Persistent host permission already granted for {Patterns}", string.Join(", ", matchPatterns));
                }
                else {
                    _logger.LogInformation("KERIAuth BW: Persistent host permission not granted for {Patterns}. Will inject for current tab only using activeTab.", string.Join(", ", matchPatterns));
                }
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "KERIAuth BW: Could not check persistent host permissions - will use activeTab");
            }

            // NOTE: Content script injection is now handled in app.js beforeStart() hook
            // This preserves the user gesture required for permission requests
            // The JavaScript handler runs before this C# handler and handles all injection logic
            _logger.LogInformation("KERIAuth BW: Content script injection handled by app.js - no action needed in C# handler");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling action click");
        }
    }

    /// <summary>
    /// Build match patterns from the tab's URL, suitable for both:
    /// - chrome.permissions.{contains,request}({ origins: [...] })
    /// - chrome.scripting.registerContentScripts({ matches: [...] })
    ///
    /// Notes:
    /// - Match patterns don't include ports; they'll match any port on that host.
    /// - Only http/https are supported for injection.
    /// </summary>
    private List<string> BuildMatchPatternsFromTabUrl(string tabUrl) {
        try {
            var uri = new Uri(tabUrl);
            if (uri.Scheme == "http" || uri.Scheme == "https") {
                // Exact host only - returns pattern like "https://example.com/*"
                return [$"{uri.Scheme}://{uri.Host}/*"];
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error parsing tab URL: {TabUrl}", tabUrl);
        }
        return [];
    }

    // onRemoved fires when a tab is closed
    [JSInvokable]
    public async Task OnTabRemovedAsync(int tabId, object removeInfoObj) {
        try {
            _logger.LogInformation("OnTabRemovedAsync event handler called");

            // Deserialize to strongly-typed object
            var removeInfoJson = JsonSerializer.Serialize(removeInfoObj);
            var removeInfo = JsonSerializer.Deserialize<TabRemoveInfo>(removeInfoJson);

            _logger.LogInformation("Tab removed: {TabId}, WindowId: {WindowId}, WindowClosing: {WindowClosing}",
                tabId, removeInfo?.WindowId, removeInfo?.IsWindowClosing);

            // Remove any connections associated with this tab
            var connectionToRemove = _pageCsConnections
                .Where(kvp => kvp.Value.TabId == tabId)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(connectionToRemove)) {
                _pageCsConnections.TryRemove(connectionToRemove, out _);
                _logger.LogInformation("Removed connection for closed tab: {TabId}", tabId);
            }
            // Note: No need for separate injection tracking - port connection removal handles cleanup
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling tab removal");
        }
    }

    // webNavigation.onComplete event fires when tabs navigate.
    // Typical use: Inject scripts, block/redirect, monitor
    public static async Task OnWebNavCompletedAsync() {
        await Task.Delay(0);
    }

    // webRequest.onCompleted event fires when webRequests are made and finish.
    // Typical use: Inject scripts, block/redirect, monitor
    public static async Task OnWebReqCompletedAsync() {
        await Task.Delay(0);
    }

    // onSuspend event fires just before the background worker is unloaded (idle ~30s).
    // Typical use: Save in-memory state, cleanup, flush logs. ... quickly (though you get very little time).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendAsync() {
        try {
            // TODO P2 needs implementation
            ;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onSuspend");
        }
    }

    // OnSuspendCanceled event fires if a previously pending unload is canceled (e.g., because a new event kept the worker alive).
    // Parameters: none
    [JSInvokable]
    public async Task OnSuspendCanceledAsync() {
        try {
            // TODO P2 needs implementation
            ;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling onSuspend");
        }
    }

    private async Task HandleInstallAsync() {
        try {
            var installUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab&reason=install";
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = installUrl
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling install");
            throw;
        }
    }

    private async Task HandleUpdateAsync(string previousVersion) {
        try {
            // Note this will be triggered by a Chrome Web Store push,
            // or, when sideloading in development, by installing an updated release per the manifest or a Refresh in DevTools.
            var currentVersion = WebExtensions.Runtime.GetManifest().GetProperty("version").ToString() ?? DefaultVersion;
            _logger.LogInformation("Extension updated from {Previous} to {Current}", previousVersion, currentVersion);

            var updateDetails = new UpdateDetails {
                Reason = UpdateReason,
                PreviousVersion = previousVersion,
                CurrentVersion = currentVersion,
                Timestamp = DateTime.UtcNow.ToString("O")
            };
            await WebExtensions.Storage.Local.Set(new { UpdateDetails = updateDetails });

            var updateUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab&reason=update";
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = updateUrl
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling update");
        }
    }


    private async Task<object?> HandleUnknownMessageAsync(string? action) {
        _logger.LogInformation("HandleUnknownMessageAsync called for action: {Action}", action);
        _logger.LogWarning("Unknown message action: {Action}", action);
        await Task.CompletedTask;
        return null;
    }

    private async Task<object> HandleLockAppMessageAsync() {
        try {
            _logger.LogInformation("HandleLockAppMessageAsync called");
            _logger.LogInformation("Lock app message received - app should be locked");

            // The InactivityTimerService handles the actual locking logic
            _logger.LogInformation("App lock request processed");
            return new { success = true, message = "App locked" };
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling lock app message");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task<object> HandleSystemLockDetectedAsync() {
        try {
            _logger.LogInformation("HandleSystemLockDetectedAsync called");
            _logger.LogWarning("System lock/suspend/hibernate detected in background worker");

            // Send lock message to all connected tabs/apps
            try {
                await _webExtensionsApi.Runtime.SendMessage(new { action = LockAppAction });
                _logger.LogInformation("Sent LockApp message due to system lock detection");
            }
            catch (Exception ex) {
                _logger.LogInformation(ex, "Could not send LockApp message (expected if no pages open)");
            }

            return new { success = true, message = "System lock detected and app locked" };
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling system lock detection");
            return new { success = false, error = ex.Message };
        }
    }





    /*








                    // Request permission from the user
                    // var hasPermission = await RequestOriginPermissionAsync(origin);
                    if (hasPermission) {
                        _logger.LogInformation("Permission granted for: {Origin}", origin);
                        await UseActionPopupAsync(tabId);
                    }
                    else {
                        _logger.LogInformation("Permission denied for: {Origin}", origin);
                        await CreateExtensionTabAsync();
                    }
                }
                else {
                    // If user clicks on the action icon on a page already allowed permission, 
                    // but for an interaction not initiated from the content script
                    await CreateExtensionTabAsync();
                    // TODO P2: Consider implementing useActionPopup(tabId) for direct popup interaction
                }

                // Clear the popup url for the action button, if it is set, 
                // so that future use of the action button will also trigger this same handler
                await WebExtensions.Action.SetPopup(new() { Popup = "" });

                return;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error handling action click on web page");
            }
        }
        */

    private async Task<bool> CheckOriginPermissionAsync(string origin) {
        try {
            _logger.LogInformation("Checking permission for origin: {Origin}", origin);

            // Use JavaScript helper module for permissions (CSP-compliant)
            // NOTE: WebExtensions.Net.Permissions API has type conversion issues - see RequestOriginPermissionAsync for details
            var permissionsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PermissionsHelper.js");
            var hasPermission = await permissionsModule.InvokeAsync<bool>("PermissionsHelper.contains",
                new { origins = new[] { origin } });

            _logger.LogInformation("Permission check result for {Origin}: {HasPermission}", origin, hasPermission);
            return hasPermission;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error checking origin permission for {Origin}", origin);
            return false;
        }
    }

    private async Task<bool> RequestOriginPermissionAsync(string origin) {
        try {
            _logger.LogInformation("Requesting permission for origin: {Origin}", origin);

            var isGranted = false;
            /*
                        var pt = new AnyPermissions
                        {
                            Origins = [new MatchPattern( new MatchPatternRestricted(origin))]
                        };
                        var isGranted = await WebExtensions.Permissions.Contains(pt);
                        _logger.LogInformation("Current permission for {Origin}: {IsGranted}", origin, isGranted);
            */
            var ptt = new PermissionsType {
                Origins = [new MatchPattern(new MatchPatternRestricted(origin))]
            };
            if (!isGranted) {
                // prompt via browser UI for permission
                isGranted = await WebExtensions.Permissions.Request(ptt);
            }
            _logger.LogInformation("Permission request result for {Origin}: {IsGranted}", origin, isGranted);
            return isGranted;
        }
        catch (Microsoft.JSInterop.JSException e) {
            _logger.LogError("BW: ... . {e}{s}", e.Message, e.StackTrace);
            throw;
        }
        catch (System.Runtime.InteropServices.JavaScript.JSException e) {
            _logger.LogError("BW: ... 2 . {e}{s}", e.Message, e.StackTrace);
            throw;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error requesting origin permission for {Origin}", origin);
            return false;
        }
    }

    private async Task UseActionPopupAsync(int tabId) {
        try {
            _logger.LogInformation("Using action popup for tab: {TabId}", tabId);

            // Set the popup for this specific tab to show the extension interface
            await WebExtensions.Action.SetPopup(new() {
                Popup = "index.html?environment=popup"
                // Note: TabId may not be supported in SetPopupDetails - this will set for all tabs
            });

            _logger.LogInformation("Action popup configured for tab: {TabId}", tabId);
            // Programmatically open the popup by simulating a click on the action button
            WebExtensions.Action.OpenPopup();
            _logger.LogInformation("Action popup opened for tab: {TabId}", tabId);
            // clear the popup after use, so future clicks trigger the OnActionClicked handler again
            await WebExtensions.Action.SetPopup(new() { Popup = "" });
            _logger.LogInformation("Action popup cleared for future clicks on tab: {TabId}", tabId);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting up action popup for tab {TabId}", tabId);
        }
    }

    private async Task CreateExtensionTabAsync() {
        try {
            var tabUrl = _webExtensionsApi.Runtime.GetURL("index.html") + "?environment=tab";
            var cp = new WebExtensions.Net.Tabs.CreateProperties {
                Url = tabUrl
                // TODO P2 use the same tab identifier, so we don't get multiple tabs
            };
            var res = await WebExtensions.Tabs.Create(cp) ?? throw new AggregateException("could not create tab");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error creating extension tab");
        }
    }

    private async Task SetUpPortMessageListenerAsync(object portObj, string connectionId, bool isContentScript) {
        try {
            _logger.LogInformation("SetUpPortMessageListenerAsync called for {ConnectionId}", connectionId);

            // Use WebExtensionsApi to properly bind the port object to JSRuntime
            // This follows the same pattern as other WebExtensions.Net APIs in the class
            var webExtPort = await CreateBoundWebExtensionsPortAsync(portObj);

            if (webExtPort?.OnMessage != null) {
                // Set up message listener using WebExtensions.Net OnMessage event
                // Explicitly cast to resolve ambiguity between different MessageSender types
                webExtPort.OnMessage.AddListener((Func<object, WebExtensions.Net.Runtime.MessageSender, Action, bool>)((message, sender, sendResponse) => {
                    try {
                        _logger.LogDebug("Port message received for connection {ConnectionId}", connectionId);
                        // Handle the message asynchronously without blocking the event handler
                        _ = Task.Run(async () => {
                            try {
                                await OnPortMessageReceived(connectionId, message);
                            }
                            catch (Exception ex) {
                                _logger.LogError(ex, "Error handling port message for {ConnectionId}", connectionId);
                            }
                        });
                        return true; // Indicate that the message was handled
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Error in port message event handler for {ConnectionId}", connectionId);
                        return false;
                    }
                }));

                _logger.LogInformation("Set up WebExtensions.Net port message listener for {ConnectionId}, IsContentScript: {IsContentScript}",
                    connectionId, isContentScript);
            }
            else {
                _logger.LogWarning("Failed to create bound WebExtensions.Net Port object for {ConnectionId}", connectionId);

                // Fallback to JavaScript interop if WebExtensions.Net binding fails
                await SetUpPortMessageListenerJSFallbackAsync(portObj, connectionId, isContentScript);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting up port message listener");

            // Fallback to JavaScript interop
            try {
                await SetUpPortMessageListenerJSFallbackAsync(portObj, connectionId, isContentScript);
            }
            catch (Exception fallbackEx) {
                _logger.LogError(fallbackEx, "Fallback JavaScript interop also failed for {ConnectionId}", connectionId);
            }
        }
    }

    /// <summary>
    /// Creates a properly bound WebExtensions.Net Port object using WebExtensionsApi
    /// </summary>
    private async Task<WebExtensions.Net.Runtime.Port?> CreateBoundWebExtensionsPortAsync(object portObj) {
        try {
            // WebExtensions.Net objects need to be properly bound to JSRuntime to function
            // Since we can't easily bind existing JS objects to WebExtensions.Net classes,
            // we'll return null to trigger the JavaScript fallback approach
            // This is the most reliable approach for port message handling in this context
            _ = portObj;
            _logger.LogDebug("WebExtensions.Net Port binding not implemented for runtime JS objects, using JS fallback");
            return null;
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Error in CreateBoundWebExtensionsPortAsync");
            return null;
        }
    }


    /// <summary>
    /// JavaScript fallback for port message listening when WebExtensions.Net binding fails
    /// Uses secure TypeScript module instead of eval() to comply with CSP
    /// </summary>
    private async Task SetUpPortMessageListenerJSFallbackAsync(object portObj, string connectionId, bool isContentScript) {
        try {
            _logger.LogInformation("Setting up JavaScript fallback port message listener for {ConnectionId}", connectionId);

            // Use TypeScript module for secure port message handling (no eval())
            // Following CLAUDE.md security constraints: "NEVER use eval() or any form of dynamic code evaluation"
            var portMessageModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PortMessageHelper.js");

            // Access the exported PortMessageHelper class from the module (ES6 class export)
            // Use direct property access to get the class from the module
            await portMessageModule.InvokeVoidAsync("PortMessageHelper.setupPortMessageListener",
                portObj, connectionId, _dotNetObjectRef);

            _logger.LogInformation("JavaScript fallback port message listener set up for {ConnectionId}, IsContentScript: {IsContentScript}",
                connectionId, isContentScript);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error setting up JavaScript fallback port message listener for {ConnectionId}", connectionId);
            throw;
        }
    }

    private async Task SendPortMessageAsync(object portObj, object message) {
        try {
            _logger.LogInformation("SendPortMessageAsync called");

            // Use TypeScript module for secure port message sending (no eval())
            // Following CLAUDE.md security constraints: "NEVER use eval() or any form of dynamic code evaluation"
            var portMessageModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PortMessageHelper.js");

            // Access the exported PortMessageHelper class from the module (ES6 class export)
            // Use direct property access to call the static method
            var success = await portMessageModule.InvokeAsync<bool>("PortMessageHelper.sendPortMessage", portObj, message);

            if (success) {
                _logger.LogInformation("Sent port message: {Message}", JsonSerializer.Serialize(message));
            }
            else {
                _logger.LogWarning("Failed to send port message: {Message}", JsonSerializer.Serialize(message));
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error sending port message");
        }
    }

    /// <summary>
    /// JavaScript-invokable method called when a port message is received
    /// </summary>
    [JSInvokable]
    public async Task OnPortMessageReceived(string connectionId, object messageObj) {
        try {
            _logger.LogInformation("OnPortMessageReceived called for {ConnectionId}", connectionId);

            // Deserialize to strongly-typed PortMessage
            var messageJson = JsonSerializer.Serialize(messageObj);
            var message = JsonSerializer.Deserialize<PortMessage>(messageJson);

            if (message == null) {
                _logger.LogWarning("Failed to deserialize port message from {ConnectionId}", connectionId);
                return;
            }

            _logger.LogInformation("Port message received from {ConnectionId}: Type={MessageType}",
                connectionId, message.Type);

            // Get the connection details
            if (_pageCsConnections.TryGetValue(connectionId, out var connection)) {
                await HandlePortMessageAsync(connectionId, message, connection);
            }
            else {
                _logger.LogWarning("No connection found for {ConnectionId}", connectionId);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port message from {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// JavaScript-invokable method called when a port is disconnected
    /// </summary>
    [JSInvokable]
    public async Task OnPortDisconnected(string connectionId) {
        try {
            _logger.LogInformation("OnPortDisconnected called for {ConnectionId}", connectionId);

            // Remove the connection from our tracking
            if (_pageCsConnections.TryRemove(connectionId, out var connection)) {
                _logger.LogInformation("Removed disconnected connection {ConnectionId} from tab {TabId}",
                    connectionId, connection.TabId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port disconnection for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Handles incoming port messages based on their type and connection
    /// </summary>
    private async Task HandlePortMessageAsync(string connectionId, PortMessage message, CsConnection connection) {
        try {
            _logger.LogInformation("HandlePortMessageAsync: Processing message type '{MessageType}' from {ConnectionId}",
                message.Type, connectionId);

            // Handle different message types
            switch (message.Type) {
                case "keepAlive":
                    await HandleKeepAliveMessageAsync(connectionId);
                    break;

                case "authRequest":
                    await HandleAuthRequestMessageAsync(connectionId, message, connection);
                    break;

                case "statusUpdate":
                    await HandleStatusUpdateMessageAsync(connectionId, message);
                    break;

                default:
                    _logger.LogWarning("Unknown port message type '{MessageType}' from {ConnectionId}",
                        message.Type, connectionId);
                    break;
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error handling port message type '{MessageType}' from {ConnectionId}",
                message.Type, connectionId);
        }
    }

    private async Task HandleKeepAliveMessageAsync(string connectionId) {
        _logger.LogInformation("Keep-alive message received from {ConnectionId}", connectionId);

        // Send keep-alive response
        var response = new PortMessage(
            Type: "keepAliveResponse",
            Data: new { timestamp = DateTime.UtcNow.ToString("O") }
        );

        if (_pageCsConnections.TryGetValue(connectionId, out var connection)) {
            // TODO: Use Port.PostMessage directly instead of SendPortMessageAsync
            // await SendPortMessageAsync(connection.Port, response);
            connection.Port.PostMessage(response);
        }
    }

    private async Task HandleAuthRequestMessageAsync(string connectionId, PortMessage message, CsConnection connection) {
        _logger.LogInformation("Authentication request received from {ConnectionId} on tab {TabId}",
            connectionId, connection.TabId);

        // TODO: Implement authentication request handling
        // This would typically involve:
        // 1. Validating the request
        // 2. Checking user permissions
        // 3. Processing through SignifyService
        // 4. Sending response back through port
        _ = message;
        await Task.CompletedTask;
    }

    private async Task HandleStatusUpdateMessageAsync(string connectionId, PortMessage message) {
        _logger.LogInformation("Status update received from {ConnectionId}: {Data}",
            connectionId, JsonSerializer.Serialize(message.Data));

        // TODO: Implement status update handling
        // This could update UI state or trigger other background actions

        await Task.CompletedTask;
    }


    /// <summary>
    /// Handles SELECT_AUTHORIZE_AID message - opens popup for user to select and authorize an AID
    /// </summary>
    private async Task HandleSelectAuthorizeAsync(CsBwMsg msg, WebExtensions.Net.Runtime.Port csTabPort) {
        try {
            _logger.LogInformation("BW HandleSelectAuthorize: {Message}", JsonSerializer.Serialize(msg));

            var sender = csTabPort.Sender;
            if (sender?.Tab?.Id == null) {
                _logger.LogWarning("BW HandleSelectAuthorize: no tabId found");
                return;
            }

            var tabId = sender.Tab.Id.Value;
            // Get origin from sender - WebExtensions.Net.Runtime.MessageSender has Url property
            var origin = sender.Url ?? "unknown";
            var jsonOrigin = JsonSerializer.Serialize(origin);

            _logger.LogInformation("BW HandleSelectAuthorize: tabId: {TabId}, message: {Message}, origin: {Origin}",
                tabId, JsonSerializer.Serialize(msg), jsonOrigin);

            var encodedMsg = SerializeAndEncode(msg);
            await UseActionPopupAsync(tabId, [
                new QueryParam("message", encodedMsg),
                new QueryParam("origin", jsonOrigin),
                new QueryParam("popupType", "SelectAuthorize")
            ]);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in HandleSelectAuthorize");
        }
    }

    /// <summary>
    /// Handles sign request messages from content scripts.
    /// Based on signify-browser-extension handleFetchSignifyHeaders:
    /// https://github.com/WebOfTrust/signify-browser-extension/blob/67ce30fa671e39cba6192e2f8f826ccbb424e37a/src/pages/background/handlers/resource.ts#L40
    /// </summary>
    private async Task HandleSignRequestAsync(CsBwMsg msg, WebExtensions.Net.Runtime.Port csTabPort) {
        try {
            _logger.LogInformation("BW HandleSignRequest: {Message}", JsonSerializer.Serialize(msg));

            var sender = csTabPort.Sender;
            if (sender?.Tab?.Id == null) {
                _logger.LogWarning("BW HandleSignRequest: no tabId found");
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: "No tab ID found"
                );
                csTabPort.PostMessage(errorMsg);
                return;
            }

            var tabId = sender.Tab.Id.Value;
            var origin = sender.Url ?? "unknown";

            // Extract origin domain from URL
            string originDomain = origin;
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) {
                originDomain = $"{originUri.Scheme}://{originUri.Host}";
                if (!originUri.IsDefaultPort) {
                    originDomain += $":{originUri.Port}";
                }
            }

            _logger.LogInformation("BW HandleSignRequest: tabId: {TabId}, origin: {Origin}", tabId, originDomain);

            // Deserialize payload to extract method and url
            var payloadJson = JsonSerializer.Serialize(msg.Payload);
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson, PortMessageJsonOptions);

            if (payload == null) {
                _logger.LogWarning("BW HandleSignRequest: failed to deserialize payload");
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: "Invalid payload"
                );
                csTabPort.PostMessage(errorMsg);
                return;
            }

            // Extract method and url from payload
            if (!payload.TryGetValue("method", out var methodElement)) {
                _logger.LogWarning("BW HandleSignRequest: no method in payload");
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: "Method not specified"
                );
                csTabPort.PostMessage(errorMsg);
                return;
            }

            if (!payload.TryGetValue("url", out var urlElement)) {
                _logger.LogWarning("BW HandleSignRequest: no url in payload");
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: "URL not specified"
                );
                csTabPort.PostMessage(errorMsg);
                return;
            }

            var method = methodElement.GetString() ?? "GET";
            var rurl = urlElement.GetString() ?? "";

            if (string.IsNullOrEmpty(rurl)) {
                _logger.LogWarning("BW HandleSignRequest: url is empty");
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: "URL is empty"
                );
                csTabPort.PostMessage(errorMsg);
                return;
            }

            // Get signed headers from signify service
            try {
                // Check if we're connected to KERIA
                // TODO P1 implement connection status caching to avoid repeated calls
                /*
                var connectResult = await _signifyService.Connect();
                if (connectResult.IsFailed) {
                    _logger.LogWarning("BW HandleSignRequest: not connected to KERIA");
                    var errorMsg = new BwCsMsg(
                        type: BwCsMsgTypes.REPLY,
                        requestId: msg.RequestId,
                        error: "Not connected to KERIA"
                    );
                    csTabPort.PostMessage(errorMsg);
                    return;
                }
                */

                // Get session/identifier for this origin and tab
                // TODO P2 For now, we'll use the remembered prefix from website config
                string? aidName = null;

                if (Uri.TryCreate(originDomain, UriKind.Absolute, out var websiteOriginUri)) {
                    var websiteConfigResult = await _websiteConfigService.GetOrCreateWebsiteConfig(websiteOriginUri);

                    if (websiteConfigResult.IsSuccess &&
                        websiteConfigResult.Value.websiteConfig1?.RememberedPrefixOrNothing != null) {

                        var prefix = websiteConfigResult.Value.websiteConfig1.RememberedPrefixOrNothing;
                        var aidNameRes = await _signifyService.GetNameByPrefix2(prefix);
                        if (aidNameRes != null && aidNameRes.IsSuccess) {
                            aidName = aidNameRes.Value;
                            _logger.LogInformation("BW HandleSignRequest: found AID name {AidName} for origin {Origin}", aidName, originDomain);
                        }
                    }
                }

                if (string.IsNullOrEmpty(aidName)) {
                    _logger.LogWarning("BW HandleSignRequest: no identifier configured for origin {Origin}", originDomain);
                    var errorMsg = new BwCsMsg(
                        type: BwCsMsgTypes.REPLY,
                        requestId: msg.RequestId,
                        error: $"No identifier configured for {originDomain}"
                    );
                    csTabPort.PostMessage(errorMsg);
                    return;
                }

                // Prepare headers dictionary for the request
                var headersDict = new Dictionary<string, string>();
                if (payload.TryGetValue("headers", out var headersElement)) {
                    try {
                        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersElement.GetRawText());
                        if (headers != null) {
                            headersDict = headers;
                        }
                    }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "BW HandleSignRequest: failed to parse headers");
                    }
                }

                // Call signify-ts to get signed headers
                var headersDictJson = JsonSerializer.Serialize(headersDict);
                var signedHeadersJson = await Signify_ts_shim.GetSignedHeaders(
                    originDomain,
                    rurl,
                    method,
                    headersDictJson,
                    aidName
                );

                var signedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(signedHeadersJson);

                if (signedHeaders == null) {
                    _logger.LogWarning("BW HandleSignRequest: failed to get signed headers");
                    var errorMsg = new BwCsMsg(
                        type: BwCsMsgTypes.REPLY,
                        requestId: msg.RequestId,
                        error: "Failed to generate signed headers"
                    );
                    csTabPort.PostMessage(errorMsg);
                    return;
                }

                _logger.LogInformation("BW HandleSignRequest: successfully generated signed headers with {Count} entries",
                    signedHeaders.Count);

                // Send success response with signed headers
                var replyMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    payload: signedHeaders
                );

                csTabPort.PostMessage(replyMsg);
                _logger.LogInformation("BW HandleSignRequest: sent signed headers to tab");
            }
            catch (Exception ex) {
                _logger.LogError(ex, "BW HandleSignRequest: error getting signed headers");
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: $"Error: {ex.Message}"
                );
                csTabPort.PostMessage(errorMsg);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in HandleSignRequest");
            try {
                var errorMsg = new BwCsMsg(
                    type: BwCsMsgTypes.REPLY,
                    requestId: msg.RequestId,
                    error: $"Internal error: {ex.Message}"
                );
                csTabPort.PostMessage(errorMsg);
            }
            catch (Exception sendEx) {
                _logger.LogError(sendEx, "Failed to send error response");
            }
        }
    }

    /// <summary>
    /// Serializes an object to JSON and URL-encodes it
    /// </summary>
    private static string SerializeAndEncode(object obj) {
        var jsonString = JsonSerializer.Serialize(obj);
        var encodedString = Uri.EscapeDataString(jsonString);
        return encodedString;
    }

    /// <summary>
    /// Opens an action popup for the specified tab with query parameters
    /// </summary>
    private async Task UseActionPopupAsync(int tabId, List<QueryParam> queryParams) {
        try {
            _logger.LogInformation("BW UseActionPopup acting on tab {TabId}", tabId);

            // Add environment parameter
            queryParams.Add(new QueryParam("environment", "ActionPopup"));

            // Build URL with encoded query strings
            var url = CreateUrlWithEncodedQueryStrings("./index.html", queryParams);

            // Set popup URL (note: SetPopup applies globally, not per-tab in Manifest V3)
            await WebExtensions.Action.SetPopup(new() {
                Popup = url
            });

            // Open the popup
            try {
                WebExtensions.Action.OpenPopup();
                _logger.LogInformation("BW UseActionPopup succeeded");
            }
            catch (Exception ex) {
                // Note: openPopup() sometimes throws even when successful
                _logger.LogDebug(ex, "BW UseActionPopup openPopup() exception (may be expected)");
            }

            // Clear the popup so future clicks trigger OnActionClicked handler
            await WebExtensions.Action.SetPopup(new() { Popup = "" });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error in UseActionPopup for tab {TabId}", tabId);
        }
    }

    /// <summary>
    /// Creates a URL with encoded query string parameters
    /// </summary>
    private string CreateUrlWithEncodedQueryStrings(string baseUrl, List<QueryParam> queryParams) {
        var fullUrl = _webExtensionsApi.Runtime.GetURL(baseUrl);
        var uriBuilder = new UriBuilder(fullUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

        foreach (var param in queryParams) {
            if (IsValidKey(param.Key)) {
                // Parameters are already URL-encoded by the caller where needed
                query[param.Key] = param.Value;
            }
            else {
                _logger.LogWarning("BW Invalid key skipped: {Key}", param.Key);
            }
        }

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    /// <summary>
    /// Validates that a query parameter key contains only safe characters
    /// </summary>
    private static bool IsValidKey(string key) {
        // Allow alphanumeric characters, hyphens, and underscores
        return System.Text.RegularExpressions.Regex.IsMatch(key, @"^[a-zA-Z0-9\-_]+$");
    }

    private string GetAuthorityFromUrl(string url) {
        try {
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return uri.Authority;
            }
        }
        catch (Exception ex) {
            _logger.LogInformation(ex, "Error extracting authority from URL: {Url}", url);
        }

        return "unknown";
    }

    /// <summary>
    /// Helper record for query parameters
    /// </summary>
    private sealed record QueryParam(string Key, string Value);

    // Supporting classes
    private sealed class CsConnection {
        public WebExtensions.Net.Runtime.Port Port { get; set; } = default!;
        public int TabId { get; set; }
        public string PageAuthority { get; set; } = "?";
    }

    private sealed class BlazorAppConnection {
        public WebExtensions.Net.Runtime.Port Port { get; set; } = default!;
        public string ConnectionId { get; set; } = default!;
        public int TabId { get; set; }
        public string PortType { get; set; } = default!; // BA_TAB, BA_POPUP, BA_SIDEPANEL
    }

    private sealed class UpdateDetails {
        public string Reason { get; set; } = "";
        public string PreviousVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public void Dispose() {
        try {
            _dotNetObjectRef?.Dispose();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error disposing BackgroundWorker");
        }
        GC.SuppressFinalize(this);
    }
}
