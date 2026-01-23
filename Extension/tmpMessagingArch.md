# Port-Based Messaging Architecture

This document defines the **port-based messaging architecture** for the KERI Auth browser extension where:

* **Content Scripts (CS)** run in web pages (TypeScript, bundled via esbuild)
* **Extension App** runs as popup, side panel, or extension tab (C# Blazor WASM)
* **BackgroundWorker (BW)** is a service worker implemented in C# WASM

The design provides:

* Reliable **lifecycle detection** (cleanup when App or tabs close)
* **Multi-tab and multi-frame isolation**
* Deterministic **routing and port session management**
* Robustness against **MV3 service worker suspension/restart**

---

## 1. High-Level Goals

1. **Direct App ↔ BW communication**
   The App is an extension page and connects directly to the BackgroundWorker using `chrome.runtime.connect()`. There is **no App ↔ Content Script messaging**.

2. **Per-tab isolation**
   Each tab (and optionally each frame) is treated as its own port session.

3. **Lifecycle awareness**
   The BW must be able to detect when:
   * A popup/side panel closes
   * A tab closes or navigates
   * A content script is destroyed

4. **MV3 compatibility**
   The BW may be suspended and restarted by Chrome. The system must recover cleanly via re-handshake and storage-backed persistence.

---

## 2. Contexts

### Content Script (CS)

* Injected dynamically into web pages via `chrome.scripting.executeScript()`
* One instance per tab + frame
* Opens a **long-lived port** to the BackgroundWorker **immediately on injection**
* Written in **TypeScript** (bundled to `Extension/wwwroot/scripts/esbuild/ContentScript.js`)

### Extension App

* Runs as:
  * Popup
  * Side panel
  * Extension tab
* Opens a **long-lived port** to the BackgroundWorker
* Explicitly attaches itself to a specific port session (optionally tab-scoped)
* Written in **C# Blazor WASM** (uses WebExtensions.Net for browser APIs)

### BackgroundWorker (BW)

* Implemented in **C# WASM** (BackgroundWorker.cs)
* Hosts the authoritative **port session registry and router**
* Listens for port connections from CS and App via `WebExtensions.Runtime.OnConnect`
* Routes messages between them
* Cleans up state on disconnect or tab removal
* **Persists pending requests to session storage** for service worker restart resilience

---

## 3. Ports and Naming

### Port Names

Port names **do not need to be unique**. Each `chrome.runtime.connect()` call creates a unique Port object.

Use **fixed, human-readable names**:

| Context        | Port Name |
| -------------- | --------- |
| Content Script | `"cs"`    |
| Extension App  | `"app"`   |

### Uniqueness

Uniqueness is handled at the **application level**, not by port names:

* Content Scripts are identified by:
  * `tabId`
  * `frameId`
* App instances are identified by:
  * A randomly generated `instanceId`

### Port IDs

When a port connects, the BW generates a unique **port ID** (e.g., `Guid.NewGuid().ToString()`) to track the port internally. This port ID is used as dictionary keys in the registry instead of the `Port` object itself, which cannot reliably serve as a dictionary key across JS interop boundaries.

---

## 4. Message Contract

All communication uses a **single, strongly typed envelope**.

This contract must be implemented in:

* **TypeScript** (ContentScript only)
* **C#** (BackgroundWorker and App)

### Envelope Definition (TypeScript)

```ts
export type ContextKind = 'content-script' | 'extension-app';

export type Envelope =
  | { t: 'HELLO'; context: ContextKind; instanceId: string; tabId?: number; frameId?: number }
  | { t: 'READY'; portSessionId: string; tabId?: number; frameId?: number }
  | { t: 'ATTACH_TAB'; tabId: number; frameId?: number }
  | { t: 'DETACH_TAB' }
  | { t: 'EVENT'; portSessionId: string; name: string; data?: unknown }
  | { t: 'RPC_REQ'; portSessionId: string; id: string; method: string; params?: unknown }
  | { t: 'RPC_RES'; portSessionId: string; id: string; ok: boolean; result?: unknown; error?: string };
```

### Envelope Definition (C#)

```csharp
using System.Text.Json.Serialization;

public enum ContextKind { ContentScript, ExtensionApp }

public abstract record PortMessage(
    [property: JsonPropertyName("t")] string T
);

public record HelloMessage(
    [property: JsonPropertyName("context")] ContextKind Context,
    [property: JsonPropertyName("instanceId")] string InstanceId,
    [property: JsonPropertyName("tabId")] int? TabId = null,
    [property: JsonPropertyName("frameId")] int? FrameId = null
) : PortMessage("HELLO");

public record ReadyMessage(
    [property: JsonPropertyName("portSessionId")] string PortSessionId,
    [property: JsonPropertyName("tabId")] int? TabId = null,
    [property: JsonPropertyName("frameId")] int? FrameId = null
) : PortMessage("READY");

public record AttachTabMessage(
    [property: JsonPropertyName("tabId")] int TabId,
    [property: JsonPropertyName("frameId")] int? FrameId = null
) : PortMessage("ATTACH_TAB");

public record DetachTabMessage() : PortMessage("DETACH_TAB");

public record EventMessage(
    [property: JsonPropertyName("portSessionId")] string PortSessionId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("data")] object? Data = null
) : PortMessage("EVENT");

public record RpcRequest(
    [property: JsonPropertyName("portSessionId")] string PortSessionId,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] object? Params = null
) : PortMessage("RPC_REQ");

public record RpcResponse(
    [property: JsonPropertyName("portSessionId")] string PortSessionId,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] string? Error = null
) : PortMessage("RPC_RES");
```

### Rules

1. Every port **must send `HELLO` immediately after connecting**
2. BW responds with `READY { portSessionId }`
3. App must send `ATTACH_TAB` before sending any routed messages
4. All routed messages include a `portSessionId`

### Reliability

Port messaging within Chrome extensions is considered **reliable** - messages are delivered in order and do not require acknowledgment or timeout logic. The `port.onDisconnect` event provides definitive notification when a port is closed.

Unlike network-based RPC which requires timeouts and retry logic, port-based messaging between extension components (CS, BW, App) operates within Chrome's internal messaging system and does not suffer from network-related failures.

---

## 5. PortSession Model (BW)

The BackgroundWorker maintains an in-memory port session registry.

> **Terminology note:** "PortSession" refers specifically to the logical grouping of ports (CS + App) managed by the BackgroundWorker. This is distinct from:
> - **Storage session** (`chrome.storage.session` / `StorageArea.Session`)
> - **KERIA session** (authenticated connection via signify-ts)

### Identifiers

* **Tab Key** = `{tabId}:{frameId}` (for tab-scoped port sessions)
* **PortSessionId** = random GUID generated by BW

### PortSession Structure (C#)

```csharp
public record PortSession {
    public required Guid PortSessionId { get; init; }
    public int? TabId { get; init; }  // null for non-tab-scoped sessions
    public int? FrameId { get; init; }
    public string? ContentScriptPortId { get; set; }  // Use port ID, not Port object
    public List<string> AttachedAppPortIds { get; } = [];  // Use port IDs
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
```

### Registry (C#)

```csharp
// Primary lookup by tab key (for tab-scoped port sessions)
private readonly ConcurrentDictionary<string, PortSession> _portSessionsByTabKey = new();

// App instance tracking
private readonly ConcurrentDictionary<string, (PortSession PortSession, string PortId)> _appsByInstanceId = new();

// Port ID to port session reverse lookup (use generated IDs, not Port objects as keys)
private readonly ConcurrentDictionary<string, PortSession> _portIdToPortSession = new();

// Port ID to Port object (for sending messages)
private readonly ConcurrentDictionary<string, Port> _portsById = new();
```

**Note:** Port objects should not be used as dictionary keys because their equality semantics may not be reliable across JS interop boundaries. Instead, generate a unique port ID when a port connects and use that string as the key.

---

## 6. Connection Lifecycle

### 6.1 Content Script Startup (TypeScript)

ContentScript connects **immediately on injection**:

```ts
// ContentScript.ts - runs immediately on injection
const instanceId = crypto.randomUUID();
const port = chrome.runtime.connect({ name: 'cs' });

// Send HELLO immediately
port.postMessage({
    t: 'HELLO',
    context: 'content-script',
    instanceId
});

// Wait for READY response
port.onMessage.addListener((msg) => {
    if (msg.t === 'READY') {
        console.log('PortSession established:', msg.portSessionId);
        // Store portSessionId for future messages
    }
});

port.onDisconnect.addListener(() => {
    console.log('Port disconnected');
    // Handle reconnection if needed
});
```

BW handling:
* Reads `tabId` and `frameId` from `port.Sender`
* Creates or reuses a port session
* Responds with `READY { portSessionId }`

---

### 6.2 App Startup (C# Blazor)

The App uses WebExtensions.Net for port management:

```csharp
public class AppPortService : IAsyncDisposable {
    private readonly IWebExtensionsApi _webExtensions;
    private readonly IJSRuntime _jsRuntime;
    private Port? _port;
    private string? _portSessionId;
    private readonly string _instanceId = Guid.NewGuid().ToString();

    public async Task ConnectAsync() {
        // Connect to BackgroundWorker
        var connectInfo = new ConnectInfo { Name = "app" };
        _port = await _webExtensions.Runtime.Connect(connectInfo: connectInfo);

        // Set up message listener
        _port.OnMessage.AddListener(OnMessageReceived);
        _port.OnDisconnect.AddListener(OnDisconnected);

        // Send HELLO
        await PostMessageAsync(new HelloMessage(
            ContextKind.ExtensionApp,
            _instanceId
        ));
    }

    public async Task AttachToTabAsync(int tabId, int? frameId = null) {
        await PostMessageAsync(new AttachTabMessage(tabId, frameId));
    }

    // NOTE: WebExtensions.Net Port class doesn't have PostMessage method
    // Must use IJSRuntime to call port.postMessage() on the JS object
    private async Task PostMessageAsync(PortMessage message) {
        // _port is a JsBind.Net object reference to the JS port
        await _jsRuntime.InvokeVoidAsync(
            "postMessageToPort",
            _port,
            message
        );
    }
}
```

**Required JS helper** (in `app.ts`):

```ts
// Helper for C# to call postMessage on Port objects
window.postMessageToPort = (port: chrome.runtime.Port, message: unknown) => {
    port.postMessage(message);
};
```

---

### 6.3 App Tab Attachment Flow

The BackgroundWorker is the authoritative source for which tab triggered the popup. It receives the `OnActionClicked` event with full tab info before the popup opens.

**Flow:**

1. User clicks extension icon on Tab X
2. BW receives `OnActionClicked(tab)` → stores `tab.Id` as pending popup context
3. App (popup) connects and sends `HELLO`
4. BW responds with `READY { portSessionId, tabId }` including the originating tab
5. App sends `ATTACH_TAB { tabId }` using the tabId from READY response

**Updated READY message (C#):**

```csharp
public record ReadyMessage(string PortSessionId, int? TabId = null, int? FrameId = null)
    : PortMessage("READY");
```

**Optional validation:** App can query active tab and compare against BW's tabId. If they differ (user switched tabs during popup load), the App should use BW's authoritative value or throw if the mismatch is critical:

```csharp
var tabs = await _webExtensions.Tabs.Query(new QueryInfo {
    Active = true,
    CurrentWindow = true
});
var activeTab = tabs.FirstOrDefault();

if (activeTab?.Id != readyMessage.TabId) {
    _logger.LogWarning("Tab mismatch: active={ActiveId}, origin={OriginId}. Using BW's authoritative value.",
        activeTab?.Id, readyMessage.TabId);
}

// Always use BW's authoritative tabId
await AttachToTabAsync(readyMessage.TabId ?? 0, frameId: 0);
```

**Note:** For extension tabs (not popups), `TabId` in READY will be null since they aren't associated with a web page tab.

### 6.4 ATTACH_TAB Failure Handling

`ATTACH_TAB` **must fail** if no port session exists for the specified tab. This can happen when:
- The tab has no content script injected
- The content script has not yet connected (timing issue)
- The tab was closed before App attached

**BW handling:**

```csharp
private async Task HandleAttachTab(string appPortId, AttachTabMessage msg) {
    var tabKey = $"{msg.TabId}:{msg.FrameId ?? 0}";

    if (!_portSessionsByTabKey.TryGetValue(tabKey, out var portSession)) {
        // Send error response - no port session for this tab
        await SendErrorAsync(appPortId, "ATTACH_FAILED",
            $"No port session exists for tab {msg.TabId}. Content script may not be injected.");
        return;
    }

    // Attach App to the port session
    portSession.AttachedAppPortIds.Add(appPortId);
    _appsByInstanceId[instanceId] = (portSession, appPortId);
}
```

### 6.5 SidePanel Tab Association

When the SidePanel opens to handle a pending request (e.g., `RequestSignInPage`), the request stored in `PendingBwAppRequest` already contains the `TabId` of the originating tab:

```csharp
public record PendingBwAppRequest {
    public required string RequestId { get; init; }
    public required string Type { get; init; }
    public object? Payload { get; init; }
    public int? TabId { get; init; }  // Tab that initiated the request
    public string? TabUrl { get; init; }
    // ...
}
```

The App retrieves this from session storage and uses it for `ATTACH_TAB`, ensuring it attaches to the correct tab even if the user has switched tabs since the request was created.

---

## 7. Routing Rules

### App → CS

1. App sends `RPC_REQ` or `EVENT`
2. BW looks up port session by App attachment
3. BW forwards message to port session's CS port

### CS → App

1. CS sends `RPC_REQ` or `EVENT`
2. BW routes to all App ports attached to that port session
   * Typically only one (popup or side panel)

### BW-Initiated Messages

1. BW sends message to specific port session
2. Message delivered to both CS port and attached App ports (as appropriate)

---

## 8. Cleanup and Lifecycle Handling

### Port Disconnect

When `port.OnDisconnect` fires:

* **If CS port:**
  * Remove CS from port session
  * If no App attached → destroy port session
  * TODO P2: Consider adding a grace period before port session destruction to handle CS navigation/reload scenarios

* **If App port:**
  * Detach App from port session
  * Port session remains alive as long as CS exists

### Tab Closed

BW must listen to `chrome.tabs.onRemoved` (already implemented):

```csharp
WebExtensions.Tabs.OnRemoved.AddListener(OnTabRemovedAsync);

private async Task OnTabRemovedAsync(int tabId, RemoveInfo removeInfo) {
    // Destroy all port sessions for that tabId
    // Disconnect attached App ports
}
```

This guarantees cleanup even if disconnect events are delayed.

---

## 9. Multi-Tab Behavior

Each tab gets its own port session:

```
Tab 1 → PortSession A
Tab 2 → PortSession B
```

App explicitly chooses which tab to control via `ATTACH_TAB`.

**Active tab change handling:**

When the user switches tabs while the App (especially SidePanel) is showing tab-specific UI, the App subscribes directly to `chrome.tabs.onActivated` via WebExtensions.Net:

```csharp
// In App (e.g., BaseLayout.razor or a service)
_webExtensions.Tabs.OnActivated.AddListener(OnActiveTabChanged);

private async Task OnActiveTabChanged(ActiveInfo activeInfo) {
    if (_attachedTabId.HasValue && activeInfo.TabId != _attachedTabId) {
        // User switched away from the tab we're showing UI for
        // Dismiss tab-specific UI or navigate to a neutral page
        await DismissTabSpecificUI();
    }
}
```

Extension pages (popup, sidepanel, extension tab) have direct access to Chrome extension APIs, so the App can subscribe to browser events without needing BW to relay them.

This prevents scenarios where SidePanel shows a sign-in request for Tab A while Tab B is now active.

---

## 10. Service Worker Restart Strategy

### Approach: Storage-Backed Persistence + Re-Handshake

The current pattern of persisting pending requests to session storage is preserved:

1. **Pending requests stored in session storage** via `IPendingBwAppRequestService`
2. **All contexts reconnect** and resend `HELLO` after SW restart
3. **App resends `ATTACH_TAB`**
4. **BW reconstructs port session map** from active ports
5. **App queries session storage** for pending requests (existing pattern continues to work)

### Content Script Reconnection

When the service worker restarts, all existing port connections are severed. Content Scripts must detect this and re-establish their ports.

**Challenge:** Since ports are initiated by CS, the restarted BW cannot directly notify CS to reconnect (no port exists yet).

**Solution:** BW sends a simple `sendMessage` (not port-based) to trigger CS reconnection:

```csharp
// BackgroundWorker.cs - after SW restart during initialization
private async Task NotifyContentScriptsToReconnect() {
    // Send a broadcast message to all tabs that may have content scripts
    // CS listens for this via chrome.runtime.onMessage
    var tabs = await _webExtensions.Tabs.Query(new QueryInfo());
    foreach (var tab in tabs) {
        try {
            await _webExtensions.Tabs.SendMessage(tab.Id!.Value, new { type = "SW_RESTARTED" });
        } catch {
            // Tab may not have CS injected - ignore
        }
    }
}
```

**ContentScript.ts handling:**

```ts
// Listen for SW restart notification (in addition to port messaging)
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === 'SW_RESTARTED') {
        console.log('Service worker restarted, re-establishing port');
        reconnectPort();
        sendResponse({ ok: true });
    }
    return false;
});

function reconnectPort() {
    // Close existing port if any
    port?.disconnect();

    // Re-establish connection
    port = chrome.runtime.connect({ name: 'cs' });
    port.postMessage({
        t: 'HELLO',
        context: 'content-script',
        instanceId
    });
    // ... set up listeners as before
}
```

**Port disconnect detection (alternative/complementary):**

CS can also detect SW restart via `port.onDisconnect`:

```ts
port.onDisconnect.addListener(() => {
    if (chrome.runtime.lastError) {
        console.log('Port disconnected, likely SW restart:', chrome.runtime.lastError);
        // Attempt reconnection after brief delay
        setTimeout(reconnectPort, 100);
    }
});
```

### Why Hybrid Approach

* MV3 may suspend or restart the worker at any time
* In-memory state (port references) is lost on restart
* Storage persistence ensures pending requests survive
* App can detect pending requests via storage subscription even before BW sends runtime message

### Storage Model (existing)

```csharp
public record PendingBwAppRequest {
    public required string RequestId { get; init; }
    public required string Type { get; init; }
    public object? Payload { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int? TabId { get; init; }
    public string? TabUrl { get; init; }
}
```

---

## 11. WebExtensions.Net Port API Notes

### Available in WebExtensions.Net

| API | Supported | Notes |
|-----|-----------|-------|
| `Runtime.Connect()` | Yes | Returns `Port` object |
| `Runtime.OnConnect` | Yes | Event for incoming connections |
| `Port.OnMessage` | Yes | Event for receiving messages |
| `Port.OnDisconnect` | Yes | Event for disconnect detection |
| `Port.Disconnect` | Yes | Action to close the port |
| `Port.Name` | Yes | Port name string |
| `Port.Sender` | Yes | MessageSender with tabId, frameId |

### Requires IJSRuntime Helper

| API | Notes |
|-----|-------|
| `Port.postMessage()` | Not in WebExtensions.Net; use JS helper |

**JS Helper Implementation:**

```ts
// Extension/wwwroot/app.ts
declare global {
    interface Window {
        postMessageToPort: (port: chrome.runtime.Port, message: unknown) => void;
    }
}

window.postMessageToPort = (port, message) => {
    port.postMessage(message);
};
```

**C# Usage:**

```csharp
await _jsRuntime.InvokeVoidAsync("postMessageToPort", _port, message);
```

---

## 12. Manifest Requirements

**No changes required.** The current manifest already supports this architecture:

```json
{
  "background": {
    "service_worker": "content/BackgroundWorker.js",
    "type": "module"
  },
  "permissions": [
    "activeTab",
    "scripting",
    "storage",
    ...
  ]
}
```

* Content scripts are dynamically injected via `scripting` permission
* `activeTab` provides tab access when user clicks extension icon
* No additional permissions needed for port-based messaging

---

## 13. Migration Strategy

### Phase 1: Add Port Infrastructure

1. Create C# port message types (`PortMessage` records)
2. Add JS helper for `postMessageToPort`
3. Create `IPortService` interface and implementations for BW and App
4. Add `OnConnect` handler in BackgroundWorker

### Phase 2: ContentScript Migration

1. Update ContentScript.ts to use port-based messaging
2. Connect immediately on injection
3. Send HELLO and wait for READY
4. Route all page messages through port

**Existing message types to deprecate/migrate:**

The following existing INIT/READY message types use `sendMessage` and should be migrated to use the new port-based HELLO/READY protocol:

| File | Type | Current Usage | Migration |
|------|------|---------------|-----------|
| `ExCsInterfaces.ts` | `CsInternalMsgEnum.CS_READY` | CS→BW notification via sendMessage | Replace with port HELLO |
| `ExCsInterfaces.ts` | `CsBwMsgEnum.INIT` | CS→BW init via sendMessage | Replace with port HELLO |
| `ExCsInterfaces.ts` | `BwCsMsgEnum.READY` | BW→CS ready via sendMessage | Replace with port READY response |
| `BwCsMessages.cs` | `BwCsMessageTypes.READY` | BW→CS ready constant | Deprecate, use PortMessage.READY |
| `BwCsMessages.cs` | `ReadyMessage` record | BW→CS ready message | Replace with new `ReadyMessage(PortSessionId, ...)` |

### Phase 3: App Migration

1. Create `AppPortService` in App
2. Connect on App startup
3. Migrate `AppBwMessagingService` to use port
4. Maintain backward compatibility during transition

### Phase 4: Cleanup

1. Remove old `sendMessage`-based code paths
2. Update tests
3. Remove deprecated message types

---

## 14. Testing Checklist

### Single Tab

- [ ] Open App (popup/sidepanel/tab)
- [ ] Verify port session created
- [ ] Send RPC from App → CS
- [ ] Receive response

### Multiple Tabs

- [ ] Open two tabs with CS injected
- [ ] Attach App to tab A
- [ ] Verify only tab A receives App messages
- [ ] Switch to tab B, verify routing updates

### Lifecycle

- [ ] Close popup → App port disconnects → port session remains
- [ ] Close tab → CS port disconnects → port session destroyed
- [ ] App detects pending request via storage subscription

### Service Worker Restart

- [ ] Trigger SW restart (navigate away, wait for idle timeout)
- [ ] Open tab and App
- [ ] Verify HELLO/ATTACH_TAB rebuilds port sessions
- [ ] Verify pending requests survive and are processed
- [ ] Verify CS receives SW_RESTARTED message and reconnects

### Error Cases

- [ ] ATTACH_TAB to tab without CS → receive error response
- [ ] ATTACH_TAB to closed tab → receive error response
- [ ] App switches to tab B while showing UI for tab A → UI dismissed/updated

### Active Tab Change

- [ ] SidePanel shows request for Tab A
- [ ] User switches to Tab B
- [ ] Verify SidePanel detects change via `chrome.tabs.onActivated`
- [ ] Verify tab-specific UI is dismissed or updated

---

## 15. Summary

This architecture provides:

* Clean separation of concerns
* Strong lifecycle handling
* Multi-tab correctness
* MV3 resilience via storage-backed persistence
* A clear mapping between JavaScript ports and **C# WASM-based BackgroundWorker/App**

The design preserves the existing storage-based persistence pattern while adding port-based messaging for reliable lifecycle detection and cleaner bidirectional communication.
