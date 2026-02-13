# Architecture

High-level system architecture for the KERI Auth browser extension. For operational directives, see [CLAUDE.md](CLAUDE.md). For build instructions, see [BUILD.md](BUILD.md). For coding standards, see [CODING.md](CODING.md).

## Overview

KERI Auth is a browser extension built with:
- **C# / Blazor WebAssembly** (.NET 9.0) for the extension UI and BackgroundWorker (service worker)
- **TypeScript** for content scripts and some service worker dependencies (minimize where possible)
- **MudBlazor** for UI components
- **signify-ts** for KERI/ACDC operations via JS interop
- **polaris-web** for web page communication protocol

![KERI Auth Architecture](KERIAuthArchitecture.jpg)
[Source](https://docs.google.com/drawings/d/1xICKkvaJkS4IrOGcj_3GidkKHu1VcIrzGCN_vJSvAv4)

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
Extension/                    Main browser extension project (Blazor WASM)
  Services/                   Core services (SignifyService, Port services, Storage, etc.)
  UI/                         Blazor pages and components
  Models/                     Data models and message types
  wwwroot/                    Static web assets
    scripts/es6/              ES6 modules compiled from scripts/types and scripts/modules
    scripts/esbuild/          Bundled scripts compiled from scripts/bundles
    app.ts                    Blazor startup hooks (beforeStart/afterStarted)
  Schemas/                    vLEI credential schema definitions (local copies)
Extension.Tests/              xUnit test project
scripts/                      TypeScript source code (npm workspaces monorepo)
  types/                      Shared type definitions (@keriauth/types)
  modules/                    Simple ES6 modules compiled by tsc (@keriauth/modules)
  bundles/                    esbuild-bundled scripts with signify-ts (@keriauth/bundles)
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

## Session Lifecycle

- Session state stored in `StorageArea.Session` via `PasscodeModel` (passcode + expiration)
- BackgroundWorker is authoritative via Chrome alarms
- App contexts display informational countdown timers (may drift slightly from authoritative alarm)
- User activity detected via DOM events in App, sent as USER_ACTIVITY message to BackgroundWorker, which extends session if unlocked
- On expiration: BackgroundWorker clears session storage, locking the session

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
