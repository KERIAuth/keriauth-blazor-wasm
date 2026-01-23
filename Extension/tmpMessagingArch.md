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
public enum ContextKind { ContentScript, ExtensionApp }

public abstract record PortMessage(string T);

public record HelloMessage(ContextKind Context, string InstanceId, int? TabId = null, int? FrameId = null)
    : PortMessage("HELLO");

public record ReadyMessage(string PortSessionId, int? TabId = null, int? FrameId = null)
    : PortMessage("READY");

public record AttachTabMessage(int TabId, int? FrameId = null) : PortMessage("ATTACH_TAB");

public record DetachTabMessage() : PortMessage("DETACH_TAB");

public record EventMessage(string PortSessionId, string Name, object? Data = null) : PortMessage("EVENT");

public record RpcRequest(string PortSessionId, string Id, string Method, object? Params = null)
    : PortMessage("RPC_REQ");

public record RpcResponse(string PortSessionId, string Id, bool Ok, object? Result = null, string? Error = null)
    : PortMessage("RPC_RES");
```

### Rules

1. Every port **must send `HELLO` immediately after connecting**
2. BW responds with `READY { portSessionId }`
3. App must send `ATTACH_TAB` before sending any routed messages
4. All routed messages include a `portSessionId`

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
    public Port? ContentScriptPort { get; set; }
    public List<Port> AttachedAppPorts { get; } = [];
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
```

### Registry (C#)

```csharp
// Primary lookup by tab key (for tab-scoped port sessions)
private readonly ConcurrentDictionary<string, PortSession> _portSessionsByTabKey = new();

// App instance tracking
private readonly ConcurrentDictionary<string, (PortSession PortSession, Port Port)> _appsByInstanceId = new();

// Port to port session reverse lookup
private readonly ConcurrentDictionary<Port, PortSession> _portsToPortSession = new();
```

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

When the user switches tabs while the App (especially SidePanel) is showing tab-specific UI:
1. BW listens to `chrome.tabs.onActivated`
2. BW notifies attached App ports of the tab change
3. App (BaseLayout/DialogLayout) reactively dismisses or updates UI that's no longer relevant to the active tab

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

---

## 15. Summary

This architecture provides:

* Clean separation of concerns
* Strong lifecycle handling
* Multi-tab correctness
* MV3 resilience via storage-backed persistence
* A clear mapping between JavaScript ports and **C# WASM-based BackgroundWorker/App**

The design preserves the existing storage-based persistence pattern while adding port-based messaging for reliable lifecycle detection and cleaner bidirectional communication.
