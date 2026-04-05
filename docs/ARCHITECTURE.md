# Architecture

High-level system architecture for the KERI Auth browser extension. For operational directives, see [CLAUDE.md](../CLAUDE.md). For build instructions, see [BUILD.md](BUILD.md). For coding standards, see [CODING.md](CODING.md).

## Overview

KERI Auth is a browser extension built with:
- **C# / Blazor WebAssembly** (.NET 9.0) for the extension UI and BackgroundWorker (service worker)
- **TypeScript** for content scripts and some service worker dependencies (minimize where possible)
- **MudBlazor** for UI components
- **signify-ts** for KERI/ACDC operations via JS interop
- **polaris-web** for web page communication protocol


## Runtime Contexts

The extension runs as **multiple separate Blazor WASM runtime instances**, each with its own IJSRuntime, DI container, and service state:

### BackgroundWorker (Service Worker)
- **Singleton** — one instance, but Chrome can deactivate and restart it at any time
- Entry: `manifest.json` -> `content/BackgroundWorker.js` (generated) -> `app.ts beforeStart()` -> `Program.cs` -> `BackgroundWorker.cs`
- Manages: port sessions, RPC routing, KERIA communication, session lifecycle, storage
- Key constraint: must handle becoming inactive gracefully. In-memory state is lost on restart; persistent state lives in chrome.storage.

### App Instances (Popup / Tab / SidePanel)
- **Many** — each popup, tab, or sidepanel is a separate Blazor WASM runtime
- Entry: `index.html` -> `blazor.webassembly.js` -> `app.ts beforeStart()` -> `Program.cs` -> `App.razor`
- Communicates with BackgroundWorker via port-based messaging
- Must be resilient to BackgroundWorker becoming inactive

### Content Script
- Thin TypeScript bridge injected into web pages on demand
- Runs in isolated JavaScript context (no WASM)
- Bridges web page and extension via polaris-web protocol
- **Never handles sensitive data** (passcode, keys)

## Project Layout

```
Extension/                        # Main Blazor WASM project (App + BackgroundWorker runtimes)
  Program.cs                      # WASM entry point; DI registration, runtime mode selection
  BackgroundWorker.cs             # Service worker Blazor component (after generated backgroundWorker.js)
  AppConfig.cs                    # App configuration and feature flags
  Routes.cs                       # Blazor route definitions
  Services/                       # Core application services
    AppCache.cs                   # In-memory state cache (many app states live here)
    SessionManager.cs             # Session timeout and lifecycle
    IdentifierService.cs          # KERI identifier CRUD
    SchemaService.cs              # credential schemas registry for vLEI and others
    WebsiteConfigService.cs       # Per-website configuration
    WebauthnService.cs            # WebAuthn/passkey operations
    Port/                         # Port-based messaging between runtimes
      BwPortService.cs            # BackgroundWorker-side port handler
      AppBwPortService.cs         # App-side port handler
    SignifyService/               # signify-ts client integration (JS interop)
      SignifyClientService.cs     # Main KERI/ACDC operations (~100KB)
      Models/                     # Signify response/request models
    JsBindings/                   # C#→JS interop bindings
      SignifyClientBinding.cs     # Binds to signifyClient.js
      NavigatorCredentialsBinding.cs  # WebAuthn binding
    Crypto/                       # Web Crypto API wrappers
    Storage/                      # chrome.storage abstraction
      StorageService.cs           # Typed get/set with IObservable change notifications
      StorageModelRegistry.cs     # Schema version registry for Local storage records
    NotificationPollingService/   # KERIA notification polling
    PrimeDataService/             # Bootstrap data initialization
  Models/                         # Data models and message types
    Identifier.cs                 # KERI identifier model
    Website.cs                    # Website metadata
    CachedCredentials.cs          # Cached credentials (per-credential dictionary keyed by SAID)
    CredentialViewNode.cs         # View model tree records (CredentialViewNode, CredentialViewTree)
    CredentialViewModels.cs       # View spec records (CredentialFieldSpec, CredentialViewSpec, CredentialViewOptions)
    PortSession.cs                # Port session state
    OnboardState.cs               # Onboarding state machine
    Storage/                      # Persistent storage models (IStorageModel)
  UI/                             # Blazor UI (MudBlazor)
    Layouts/                      # Layout components
    Pages/                        # Page components (~36 .razor files)
      Index.razor                 # Entry/routing page
      DashboardPage.razor         # Main dashboard
      CredentialsPage.razor       # Credential list
      ProfilesPage.razor          # KERI identifier profiles
      ConnectionsPage.razor       # Connection management
      PreferencesPage.razor       # User settings
      Request*.razor              # Pages UX to handle requests from page
    Components/                   # Reusable components (~21 .razor files)
      CredentialPanel.razor       # Credential display (compact + detail variants)
      CredentialViewTreeRenderer.razor  # Pipeline-driven credential tree renderer
      CredentialViewNodeRenderer.razor  # Recursive node renderer (Value/Dictionary/SaidReference)
      SessionStatusIndicator.razor  # Session status badge
      ProfileSelectors.razor      # Profile/identifier selector overlay
  Helper/                         # Utility helpers
    RecursiveDictionary.cs        # CESR/SAID-safe ordered dictionary (critical)
    DictionaryConverter.cs        # Dictionary conversion utilities
    Bip39MnemonicConverter.cs     # BIP39 mnemonic handling
    JsonOptions.cs                # JSON serialization configuration
  Utilities/                      # Additional utilities
  Schemas/                        # vLEI and other credential schema JSON files (local copies)
  wwwroot/                        # Static web assets
    manifest.json                 # Chrome extension manifest (MV3)
    app.ts                        # Blazor startup hooks (beforeStart/afterStarted)
    index.html                    # Entry point (?context=popup|sidepanel for non-tab contexts)
    scripts/
      es6/                        # Compiled TypeScript modules (from scripts/types + modules)
      esbuild/                    # Bundled scripts (from scripts/bundles)
        ContentScript.js          # Content script bundle (injected into web pages)
        signifyClient.js          # signify-ts client wrapper bundle
      helpers/                    # JS helpers (audio, camera/QR)

scripts/                          # TypeScript source (npm workspaces monorepo)
  types/src/                      # Shared type definitions (@keriauth/types)
    PortMessages.ts               # Port messaging protocol types
    BwCsPayloads.ts               # BackgroundWorker→ContentScript payloads
    CsBwRpcMethods.ts             # ContentScript→BackgroundWorker RPC methods
    ExCsInterfaces.ts             # Extension↔ContentScript interfaces
    storage-models.ts             # Storage type definitions
  modules/src/                    # ES6 modules compiled by tsc (@keriauth/modules)
    aesGcmCrypto.ts               # AES-GCM encryption utilities
    userActivityListener.ts       # User activity tracking
    navigatorCredentialsShim.ts   # WebAuthn shim for content script
  bundles/src/                    # esbuild-bundled scripts (@keriauth/bundles)
    ContentScript.ts              # Content script (thin bridge to web pages)
    signifyClient.ts              # signify-ts client wrapper

Extension.Tests/                  # xUnit + Moq test project
  Helper/                         # Helper class tests (RecursiveDictionary, etc.)
  Models/                         # Model serialization/roundtrip tests
  Services/                       # Service tests (SessionManager, etc.)
  Utilities/                      # Utility tests

docs/                             # Project documentation
  ARCHITECTURE.md                 # This file — system structure and flows
  BUILD.md                        # Build commands, troubleshooting, environment
  CODING.md                       # C#, TypeScript, and interop coding standards
  PAGE-CS-MESSAGES.md             # Web page ↔ content script message protocol
  POLARIS_WEB_COMPLIANCE.md       # Supported polaris-web capabilities
```

## Messaging Architecture

Three messaging boundaries:

### 1. Web Page to Content Script
- Via polaris-web protocol (window.postMessage / CustomEvent)
- See [PAGE-CS-MESSAGES.md](PAGE-CS-MESSAGES.md) for message protocol

### 2. Content Script / App to BackgroundWorker
- Via long-lived ports (`chrome.runtime.connect`)
- Handshake: CS or App opens port, sends HELLO, BW responds with READY (includes portSessionId), App sends ATTACH_TAB to bind to a tab's port session
- Messages: RPC_REQ / RPC_RES for request-response, EVENT for one-way notifications
- Port sessions group Content Script and App ports per tab

### 3. BackgroundWorker to KERIA
- Via signify-ts library (JS interop from C#)
- All cryptographic operations happen here

## State Management

### AppCache
- Primary in-memory state for the App context, captured in `AppCache.cs`
- Provides fast access to frequently-needed state without repeated storage reads

### Storage Architecture
- Unified `IStorageService` abstraction over Chrome's storage APIs (local, session, sync, managed)
- Strongly-typed record models with `required` properties
- Storage keys derived from type names (`typeof(T).Name`)
- Implements `IObservable<T>` for reactive change notifications
- Managed storage is read-only (enterprise policies)

### Service Worker State Persistence
- Manifest V3 service workers can become inactive and restart, losing in-memory state
- Prefer inferring state from existing sources (port connections, browser APIs) over maintaining separate state dictionaries
- Use `chrome.storage.session` for state that must survive restarts but not browser close
- Use `chrome.storage.local` for long-term persistence

### Notification Polling

KERIA notifications are fetched via two mechanisms that allow the service worker to become inactive between polls:

**Burst polling** — Polls every 5 seconds for 120 seconds, then stops. Triggered by:
- Successful KERIA connect or reconnect
- User activity events (throttled at 10s intervals)
- Connection invite reply (OOBI shared)
- Opening the Notifications page (only if no burst is already active)

**Recurring Chrome Alarm** — Fires every 5 minutes while session is active. Wakes the service worker for a single poll, then the SW goes inactive again. Chrome manages alarm persistence across SW restarts. Cleared when session locks.

Implementation: `NotificationPollingService` handles the actual KERIA fetch, enrichment, and fingerprint-based deduplication. `BackgroundWorker` manages burst lifecycle (`CancellationTokenSource`) and alarm creation/clearing.

### Chrome Extension APIs
- C# bindings via [WebExtensions.Net](https://github.com/mingyaulee/WebExtensions.Net) NuGet package
- Listeners registered in `BackgroundWorker.Main()` are auto-generated as module-level JavaScript by `Blazor.BrowserExtension.Analyzer`, ensuring they run before WASM boots and can wake the service worker on cold start

## Credential View Generation Pipeline

Transforms a credential (`RecursiveDictionary`) and its embedded schema into a view model tree for rendering.

### Data Flow

```
RecursiveDictionary (credential with sad, schema, chains[])
  → ClonedCredential.FromRecursiveDictionary()
  → CredentialViewPipeline.MergeAcdcAndSchema()    // walks sad + schema in parallel
  → CredentialViewPipeline.Prune()                  // applies ViewSpec + ViewOptions
  → CredentialViewTree                              // view model tree
  → CredentialViewTreeRenderer.razor                // recursive Razor rendering
```

For credentials with chained credentials (e.g., ECR → ECR Auth → LE vLEI → QVI), `BuildFullTree()` recursively applies the pipeline to each level, looking up the appropriate `CredentialViewSpec` by schema SAID.

### Key Components

| Component | Location | Role |
|-----------|----------|------|
| `ClonedCredential` | `Services/SignifyService/Models/Credential.cs` | Typed wrapper extracting sad, schema, chains[] from RecursiveDictionary |
| `CredentialViewNode` / `CredentialViewTree` | `Models/CredentialViewNode.cs` | View model records — each node carries label, format, tooltip, oneOf state, component hints |
| `CredentialViewPipeline` | `Services/CredentialViewPipeline.cs` | Static pure functions: `MergeAcdcAndSchema`, `Prune`, `BuildFullTree` |
| `CredentialViewSpecService` | `Services/CredentialViewSpecService.cs` | Loads `credentialViewSpecs.json` — per-schema field visibility, labels, formats |
| `CredentialViewNodeRenderer` | `UI/Components/CredentialViewNodeRenderer.razor` | Recursive component switching on node Kind (Value, Dictionary, SaidReference, etc.) |
| `CredentialViewTreeRenderer` | `UI/Components/CredentialViewTreeRenderer.razor` | Top-level renderer with DisclosureState for elision toggles |

### oneOf and Elision

ACDC schemas use `oneOf` for sections (`a`, `e`, `r`) that can be either a SAID digest (elided) or an expanded object (disclosed). The pipeline:
1. **Merge** detects `oneOf` in the schema and tags nodes with `IsOneOf`
2. **Prune** marks `IsOneOf` nodes as `IsElisionToggleable` when in presentation mode
3. **Renderer** shows disclosure checkboxes; toggling updates `DisclosureState` without rebuilding the tree

Chained credentials follow the same pattern — in presentation mode, entire chain sections can be disclosed or elided.

## Session Lifecycle

- Passcode held only in BackgroundWorker memory (`SessionManager._passcode`) — lost on service worker termination
- `SessionStateModel` in `StorageArea.Session` tracks expiration; `SessionManagerAlarm` (one-shot Chrome alarm) fires at expiration to lock
- `SessionKeepAliveAlarm` (periodic, every 30s) prevents Chrome from terminating the service worker during an active session. First fire is scheduled 25s after creation to close the gap before Chrome's ~30s idle timeout
- App contexts display informational countdown timers (may drift slightly from authoritative alarm)
- User activity detected via DOM events in App, sent as USER_ACTIVITY event to BackgroundWorker, which extends session if unlocked
- On expiration or service worker restart (passcode lost): BackgroundWorker clears session storage, locking the session

## JavaScript Module Loading

- `app.ts beforeStart()` runs separately in each runtime context before Blazor starts
- Modules loaded and cached by browser's native ES module system
- C# accesses pre-loaded modules via `IJSRuntime.InvokeAsync("import", path)` — instant from cache
- Fail-fast: if module loading fails, Blazor startup is prevented

## Extension Manifest (V3)

- Manifest V3 required for Chrome Web Store
- Compatibility: Chrome, Edge, Brave (minimum version per manifest.json)
- Strict CSP: no inline scripts, no eval, no dynamic code
- Minimum required permissions; host permissions requested at runtime
- Components: Background worker, content script (on-demand), Blazor WASM UI (popup, options page, extension tab, future sidepanel)
