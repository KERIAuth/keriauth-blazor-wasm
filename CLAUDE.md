# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architecture Overview

This is a KERI Auth browser extension built with:
- C# and Blazor WebAssembly (.NET 9.0) for the extension UI and BackgroundWorker (aka service worker)
- TypeScript for content scripts and some service worker dependencies
- MudBlazor for UI components
- signify-ts for KERI/ACDC operations
- polaris-web for web page communication protocol

Key project layout:
- `Extension/` - Main browser extension project (Blazor WASM)
- `Extension.Tests/` - xUnit test project
- `Extension/Services/` - Core services including SignifyService for KERI operations
- `Extension/UI/` - Blazor pages and components
- `Extension/wwwroot/scripts/` - Compiled JavaScript output (gitignored)
  - `es6/` - ES6 modules compiled from scripts/types and scripts/modules
  - `esbuild/` - Bundled scripts compiled from scripts/bundles
- `Extension/Schemas/` - vLEI credential schema definitions (local copies)
- `scripts/` - TypeScript source code (npm workspaces monorepo)
  - `types/` - Shared type definitions (@keriauth/types)
  - `modules/` - Simple ES6 modules compiled by tsc (@keriauth/modules)
  - `bundles/` - esbuild-bundled scripts with signify-ts (@keriauth/bundles)

The extension follows a multi-component architecture, including:
1. **Service Worker** - BackgroundWorker for handling extension lifecycle and message routing
2. **Content Script** - Injected into web pages, bridges page and extension communication
3. **Blazor WASM App** - UI layer for extension popup and tabs. Each runtime context has its own Blazor instance.
4. **SignifyService** - Manages KERI/ACDC operations via signify-ts JavaScript interop

Services use dependency injection with primarily singleton lifetime for state management across the extension.

### Storage Architecture

The extension uses a unified `IStorageService` abstraction over Chrome's storage APIs (local, session, sync, managed). All storage operations use strongly-typed record models with `required` properties to ensure data completeness at compile-time. Storage keys are derived from type names (`typeof(T).Name`), eliminating string-based key management. The service implements `IObservable<T>` for reactive storage change notifications across extension components.

**Key principles**:
- Type-safe models with `required` properties prevent null reference errors
- Managed storage is read-only (enterprise policies)
- Quota APIs only available for Session and Sync storage areas
- Never serialize credentials with standard JSON libraries - use `RecursiveDictionary` to preserve CESR/SAID field ordering

## JavaScript Module Loading Architecture

**CRITICAL**: The extension has **two separate Blazor WASM runtime instances**, each with its own `IJSRuntime` and module cache:

1. **BackgroundWorker Runtime** - Service worker context (loaded via `content/BackgroundWorker.js`)
2. **App Runtime** - UI context for popup/tab/sidepanel (loaded via `index.html`)

**Module Loading Pattern:**

JavaScript ES modules are loaded by `app.ts beforeStart()` hook **BEFORE** Blazor starts:

```typescript
// Extension/wwwroot/app.ts
export async function beforeStart(
    options: WebAssemblyStartOptions,
    extensions: Record<string, unknown>,
    blazorBrowserExtension: BrowserExtensionInstance
): Promise<void> {
    const mode = blazorBrowserExtension.BrowserExtension.Mode;

    if (mode === 'Background') {
        // Load modules for BackgroundWorker
        await Promise.all([
            import('./scripts/esbuild/signifyClient.js'),
            // ... other modules
        ]);
    } else if (mode === 'Standard' || mode === 'Debug') {
        // Load modules for App (includes WebAuthn)
        await Promise.all([
            import('./scripts/esbuild/signifyClient.js'),
            import('./scripts/es6/webauthnCredentialWithPRF.js'),
            // ... other modules
        ]);
    }
}
```

**How It Works:**

1. `app.ts beforeStart()` executes **separately in each runtime context**
2. Modules are loaded and cached by the browser's native module system
3. C# code accesses modules via `IJSRuntime.InvokeAsync("import", path)` - **instant return from browser cache**
4. If module loading fails, Blazor startup is prevented (fail-fast)

**Benefits:**
- ✅ Each runtime gets its own module cache
- ✅ Modules loaded before C# code needs them
- ✅ Fail-fast if modules missing
- ✅ No race conditions
- ✅ Uses native browser module caching

**C# Service Pattern:**
```csharp
// Services access pre-loaded modules from browser cache
public class MyService {
    private readonly IJSRuntime _jsRuntime;

    public async Task DoSomethingAsync() {
        // Import from browser's module cache (instant - already loaded by app.ts)
        var module = await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./scripts/es6/myModule.js"
        );
        await module.InvokeVoidAsync("myFunction");
    }
}
```

**Key Files:**
- [Extension/wwwroot/app.ts](Extension/wwwroot/app.ts) - beforeStart/afterStarted hooks, module loading
- [Extension/Program.cs](Extension/Program.cs) - Blazor startup (modules already loaded)
- [Extension/BackgroundWorker.cs](Extension/BackgroundWorker.cs) - Service worker main entry

### Blazor WASM Startup Flows

The KERI Auth browser extension uses **Blazor.BrowserExtension** to run Blazor WASM in two distinct runtime contexts:

1. **BackgroundWorker Context** - Service worker running in background
2. **App Context** - UI running in popup, tab, or sidepanel

**These are SEPARATE Blazor WASM runtime instances** with separate `IJSRuntime` instances, separate DI containers, and separate service state. In addition, the ContentScript injected into an isolated world also has a distinct IJSRuntime, although the current design of the ContentScript doesn't use WASM but could.

#### Flow 1: BackgroundWorker Context (Service Worker)

**Triggers:**
- Chrome initially loads the extension and starts the service_worker defined in `manifest.json`:

```json
"background": {
  "service_worker": "content/BackgroundWorker.js",
  "type": "module"
}
```

- The receipt of registered events, such as OnClick, OnMessage, and others will also restart the BackgroundWorker if it becomes inactive.

**Startup Sequence for BackgroundWorker Context:**

```
manifest.json
  ↓
content/BackgroundWorker.js (generated by Blazor.BrowserExtension.Build)
  ↓ imports BackgroundWorkerRunner.js
  ↓
BackgroundWorkerRunner.js (from Blazor.BrowserExtension)
  ↓ loads .NET runtime
  ↓ calls app.ts beforeStart() hook
  ↓
app.ts beforeStart(mode='Background')
  ↓ Detects mode === 'Background'
  ↓ Loads BackgroundWorker modules (signifyClient, etc.)
  ↓ Imported modules are cached in BackgroundWorker's runtime
  ↓ Sets up browser event listeners (chrome.action.onClicked, etc.)
  ↓ Returns control
  ↓
dotnet.js → blazor.webassembly.js
  ↓ initializes WASM runtime
  ↓
Program.cs (Main entry point)
  ↓ Configures services for BackgroundWorker context
  ↓
await host.RunAsync()
  ↓
BackgroundWorker.Main() method is invoked
  ↓ Has access to modules already loaded and cached
  ↓ Registers browser event listeners via WebExtensions API
  ↓
BackgroundWorker is now running (with modules ready)
```

#### Flow 2: App Context (Popup/Tab/Sidepanel)

**Trigger:**
User clicks extension icon, extension opens a ***popup***, ***tab*** or ***sidepanel***, or user navigates to the extension URL in a tab.

**Startup Sequence for SPA:**

```
index.html
  ↓ <script src="framework/blazor.webassembly.js"></script>
  ↓
blazor.webassembly.js
  ↓ loads .NET runtime
  ↓ calls app.ts hooks
  ↓
app.ts beforeStart(mode='Standard'/'Debug')
  ↓ Detects mode
  ↓ Loads App modules (signifyClient, webauthnCredentialWithPRF, etc.)
  ↓ Modules cached in App's runtime
  ↓ Returns control
  ↓
dotnet.js → WASM runtime initialization
  ↓
Program.cs (Main entry point)
  ↓ Configures services for App context
  ↓
await host.RunAsync()
  ↓
App.razor OnInitializedAsync() is invoked
  ↓ UI components initialize
  ↓
App UI is rendered and interactive
Typically, this start with the MainLayout.razor and IndexPage.razor component.
```

## Message Flow Architecture

The extension uses a multi-layered messaging system with strict boundaries:

1. **Web Page ↔ Content Script**: Via polaris-web JavaScript API protocol
   - Web page calls polaris-web methods
   - Content script validates and forwards to service worker
   - See [PAGE-CS-MESSAGES.md](./PAGE-CS-MESSAGES.md) for complete message protocol reference

2. **Content Script ↔ Service Worker**: Via chrome.runtime messaging
   - Messages must include sender tab information
   - Only active tab messages accepted during authentication

3. **Service Worker ↔ Blazor WASM**: Via AppSwMessagingService
   - Bidirectional communication for UI updates
   - State synchronization across extension components

4. **Blazor/Service Worker ↔ KERIA**: Via signify-ts library
   - KERI protocol operations
   - Credential management
   - Agent communication

**Critical**: Messages must be validated at each boundary for security. Never pass sensitive data (passcode, keys) through content script.

**Protocol Documentation:**
- **[PAGE-CS-MESSAGES.md](./PAGE-CS-MESSAGES.md)** - Complete reference for Page ↔ Content Script message protocol
- **[POLARIS_WEB_COMPLIANCE.md](./POLARIS_WEB_COMPLIANCE.md)** - Polaris-web protocol compliance status

## Development Environment

Primary development environment is Windows running WSL2. Commands work in both Windows PowerShell/Command Prompt and WSL2 bash. Claude Code operates within the WSL2 environment.

## Prerequisites

- Node.js >= 22.13.0
- npm >= 11.0.0
- .NET 9.0 SDK
- Chromium-based browser version 127+ (Chrome, Edge, or Brave)

**Check versions:**
```bash
node --version
npm --version
dotnet --version
```

## Quick Start for New Developers

### First Time Setup

**Environment**: These commands work in WSL, Linux, macOS, or Git Bash on Windows.

```bash
# 1. Clone and enter directory
cd kbw

# 2. Install dependencies and build TypeScript
cd scripts && npm install && npm run build
cd ../Extension && npm install && npm run build:app
cd ..

# 3. Build and test
dotnet build -p:Quick=true
dotnet test

# Extension package is now in: Extension/bin/Debug/net9.0/browserextension/
```

### Load Extension in Browser

After building from source code:

1. Open Chrome/Edge/Brave
2. Navigate to `chrome://extensions`
3. Enable "Developer mode" (top right)
4. Click "Load unpacked"
5. Select the directory:
   - For development: `Extension/bin/Debug/net9.0/browserextension/`
   - For production: `Extension/bin/Release/net9.0/browserextension/`

### Development Workflow Patterns

**TypeScript-only changes**:
- Use watch mode (`npm run watch` in Extension/) for automatic rebuilds
- Alternative: Manual rebuild when needed
- Always reload browser extension after changes

**C#-only changes**:
- Build with TypeScript skipped (faster for C# iteration)
- Use dotnet watch for automatic rebuilds
- Always reload browser extension after changes

**Mixed TypeScript + C# changes**:
- Full rebuild required (both build systems)
- Consider using watch mode for TypeScript in one terminal, manual C# builds in another

**Why reload required**: Browser caches extension files; must click reload button in chrome://extensions to see changes.

## Build System Architecture

### Why Two Separate Build Systems

The extension requires **two independent build systems** that must coordinate:

1. **TypeScript/JavaScript Build** (npm/esbuild)
   - **Why separate**: signify-ts and dependencies must be bundled before C# build
   - **Why esbuild**: Blazor.BrowserExtension BackgroundWorker.js generator scans for static assets at MSBuild time
   - **Output**: `Extension/wwwroot/scripts/` (becomes StaticWebAssets)

2. **C# Build** (dotnet/MSBuild)
   - **Why after JavaScript**: Requires JavaScript files to already exist in wwwroot/
   - **What it does**: Compiles Blazor WASM, packages JavaScript as StaticWebAssets, generates extension manifest
   - **Output**: Final browser extension package

### Build Flow Sequencing

```
TypeScript sources → npm build → JS in wwwroot/ → dotnet build → Extension package
```

**Critical timing issue**: The BackgroundWorker.js generator runs during `StaticWebAssetsPrepareForRun`, which happens BEFORE the MSBuild `BuildExtensionScripts` target. This is why JavaScript must exist before C# build starts.

### Build Properties and Flags

The build system uses MSBuild properties to control behavior:

- **`BuildingProject=true`**: Triggers TypeScript build before C# compilation
- **`SkipJavaScriptBuild=true`**: Skips TypeScript (faster for C#-only changes)
- **`Configuration=Release`**: Production optimizations
- **`DesignTimeBuild=true`**: Auto-set by IDEs to skip npm during IntelliSense

**Command discovery**: See `Extension/package.json` for npm scripts and `Extension/Extension.csproj` for MSBuild targets.

### Build Troubleshooting Principles

**When extension won't load**:
- Verify build output directory exists with manifest.json
- Check for JavaScript bundle files in scripts/esbuild/
- Enable verbose build logging to see which step fails

**When changes don't appear**:
- Rebuild with full TypeScript compilation if .ts files changed
- **Critical**: Hard reload extension in chrome://extensions (circular reload button)
- Browser caches extension files - refreshing popup is not sufficient

**When build fails mysteriously**:
- Close Visual Studio (can lock files on Windows)
- Clean build artifacts (dotnet clean, npm clean)
- Reinstall dependencies if package.json changed
- Check for WSL/Windows file permission conflicts

**Command reference**: See `Extension/package.json` scripts and `.csproj` MSBuild targets for available commands.

## TypeScript Coding Guidelines

**IMPORTANT: Always prefer TypeScript over JavaScript for new code.** All new browser extension scripts, modules, and interop files should be written in TypeScript (.ts) rather than JavaScript (.js). TypeScript provides better type safety, IntelliSense support, and maintainability.

**NEVER create new .js files** - Always use TypeScript (.ts) files that compile to JavaScript.

**When using JsBind.Net for interop**: Do not create new TypeScript or JavaScript files unless absolutely necessary. The JsBind.Net library (by mingyaulee) provides sufficient JavaScript interop capabilities for most browser extension needs without requiring additional script files.

### Code Style Guidelines
- Use strong types - define interfaces/types for all data structures
- Avoid use of "any" type where possible - use "unknown" if type is truly dynamic
- 2-space indentation (common TypeScript convention)
- Use `const` for immutable values, `let` for mutable ones, avoid `var`
- Prefer arrow functions for callbacks and short functions
- Use async/await over promise chains. However, note there may be some intentional promise chains in the code to avoid known race conditions or where the user-context must be maintained, such as responding to Action OnClick.
- Export types/interfaces alongside implementations
- File naming: camelCase for .ts files (e.g., contentScript.ts)
- Use ES6+ features (template literals, destructuring, spread operator)

### TypeScript Patterns for Browser Extension

#### Content Script
- Location: `scripts/bundles/src/ContentScript.ts`
- Bundled via esbuild to `Extension/wwwroot/scripts/esbuild/ContentScript.js`
- Injected conditionally into web pages
- Runs in isolated context, separate from page scripts
- Bridge between web page and extension via polaris-web

#### Signify Client
- Location: `scripts/bundles/src/signifyClient.ts`
- Provides JavaScript-C# interop layer for signify-ts
- Bundled with signify-ts dependencies via esbuild
- Paired with `Extension/Services/SignifyService/Signify_ts_shim.cs`

#### Shared Types
- Location: `scripts/types/src/`
- Contains interfaces shared between TypeScript and C# (ExCsInterfaces.ts, storage-models.ts)
- Compiled to ES6 modules for browser import

#### Message Types
Define interfaces for all chrome.runtime messages:

```typescript
interface ExtensionMessage {
  type: string;
  payload: unknown;
  sender?: chrome.runtime.MessageSender;
}
```

#### Polaris-Web Integration
- Follow protocol defined in `signify-polaris-web` package
- Implement required handlers for KERI operations
- Validate all incoming requests from web pages
- **Protocol compliance**: See [POLARIS_WEB_COMPLIANCE.md](./POLARIS_WEB_COMPLIANCE.md) for supported capabilities
- **Message protocol**: See [PAGE-CS-MESSAGES.md](./PAGE-CS-MESSAGES.md) for request/response types

## C# Coding Guidelines

### C# Code Style Guidelines

- **.NET Version**: .NET 9.0 SDK required
- **Indentation**: 4-space indentation
- **Braces**: Opening braces on same line; Use braces for all control structures, even single-line statements
- **Naming**:
  - PascalCase for classes, interfaces (with 'I' prefix), public methods and properties, enums
  - camelCase with underscore prefix for private fields (_fieldName)
  - Constants: use readonly fields or const values (prefer const for immutable values)
- **Imports**: Order by System namespaces first, then project namespaces; sort alphabetically within groups; at top of file, outside namespace declarations
- **Types**: Use explicit types; prefer `var` only when type is obvious or for complex LINQ expressions
- **Nullability**: Nullable reference types enabled - use explicit null checks; default to non-nullable; use `?` for nullable types
- **Async**: Use async/await consistently; avoid blocking calls and sync-over-async patterns; Async methods suffixed with "Async" returning Task/Task<T>
- **Comments**: Use XML docs for public APIs; inline comments for implementation details; avoid regions
- **Constructors**: Use constructors for setting properties; Avoid using public setters for properties
- **Dates and Locale**: Use UTC for all date/time values in the backend
- **Error Handling**:
  - Use FluentResults pattern with `Result<T>` for method returns; include descriptive error messages
  - Don't use exceptions for control flow, only if absolutely necessary
  - Return Result.Ok() for successful operations and Result.Fail("some message") for errors
  - Try to avoid explicit types for `Result<T>` unless necessary
  - All methods that can fail in any way should return a `Result<T>`
  - Try/catch with specific exception types
- **Data Structures**: 
  - Prefer Records over Classes for immutable data types, DTOs, value objects, and model classes
  - Use Records for: JSON serialization models, API request/response types, configuration objects, state representations
  - Records provide value equality, immutability by default, and support for `with` expressions
  - Use Classes only when mutability is required or for service implementations with behavior
  - All SignifyService models and Extension models should be Records unless there's a specific need for mutability
- **Generics**: Use generic functions when there are multiple types that will be applicable, including for message payload request and response types
- **Testing**: Test pattern: Arrange-Act-Assert with descriptive test names using xUnit
- **Code Quality**: EnforceCodeStyleInBuild enabled in project files
- **Expression-bodied**: Use expression-bodied members for simple methods

### Common Libraries

- **FluentResults**: Use for error handling and result management
- **MudBlazor**: UI component library for Blazor
- **xUnit**: Testing framework
- **System.Text.Json/Newtonsoft.Json**: JSON serialization (be aware of ordering issues with CESR/SAID)

### Application Setup

- **Dependency Injection**: Use built-in DI container; register services in `Program.cs` using `AddScoped`, `AddTransient`, or `AddSingleton` as appropriate
- **Configuration**: Use `IConfiguration` for app settings; bind to strongly-typed classes for complex settings. Create an `AppSettings.cs` and bind all common settings to that class. Avoid hard-coded values in the codebase. Use `IOptions<AppSettings>` to access the settings in the codebase.

## Use of KERI, ACDC, and CESR via signify-ts

- For understanding of the terms KERI, ACDC, OOBI, and IPEX, see the following resources:
  - <https://github.com/GLEIF-IT/vlei-trainings/blob/main/markdown/llm_context.md>
  - 
- CESR (Compact Event Streaming Representation): Self-describing binary encoding format used throughout KERI/ACDC for cryptographic primitives and data structures. Key point: preserves field ordering for deterministic serialization
- Interaction patterns for those protocols, including key management, ACDC (i.e., a credential or attestation) interactions between the roles of Issuer, Holder, and Verifier:
  - Issuer creates credential
  - Issuer issues credential to Holder
  - Holder receives credential from Issuer
  - Holder presents credential to Verifier
  - Verifier verifies credential from Holder (cryptographically based on holder's KEL and presentation signature; and validates a credential based on issuer's then-current keys, provenance of crednetials and their potential revocation)

### CRITICAL: Credential Handling and CESR/SAID Ordering

**NEVER serialize/deserialize credentials using System.Text.Json or Newtonsoft.Json** - This WILL break CESR/SAID ordering and invalidate cryptographic signatures.

**Correct approaches for handling credentials in C#:**

1. **Use RecursiveDictionary for credential data**: This type preserves insertion order required for SAID verification
   ```csharp
   // Credential received from signify-ts
   RecursiveDictionary credential = await signifyClient.GetCredential(...);

   // Store as RecursiveDictionary, NEVER serialize/deserialize
   myModel.Credential = credential;
   ```

2. **Convert to object only when passing to JavaScript**: Use JsBind.Net's ToJsObject() method
   ```csharp
   // When sending to Content Script via BackgroundWorker
   var credentialObj = credential.ToJsObject();
   await SendToContentScript(credentialObj);
   ```

3. **NEVER do this**:
   ```csharp
   // ❌ WRONG - Breaks CESR/SAID ordering
   var json = JsonSerializer.Serialize(credential);
   var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

   // ❌ WRONG - Also breaks ordering
   var json = JsonConvert.SerializeObject(credential);
   ```

**Why this matters:**
- SAID (Self-Addressing IDentifier) is a cryptographic hash of the credential content
- The hash depends on exact field ordering in the JSON representation
- Standard C# JSON serializers do not preserve insertion order
- Re-serializing scrambles field order, making the SAID invalid
- Invalid SAID = credential verification fails

**Data types for credentials:**
- Received from signify-ts: `RecursiveDictionary`
- Stored in C# models: `RecursiveDictionary`
- Passed to JavaScript: Convert using `ToJsObject()` only at message boundary
- Never use: `string`, `Dictionary<string, object>`, or any JSON serialization

### vLEI Credential Schema Definitions

Schema repository: <https://github.com/GLEIF-IT/vLEI-schema>
Local copies: `Extension/Schemas/`

Credential types and their schemas:
- **QVI** (Qualified vLEI Issuer) Credential
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/qualified-vLEI-issuer-vLEI-credential.json>
  - Local: `Extension/Schemas/qualified-vLEI-issuer-vLEI-credential.json`
  - Purpose: Issued by GLEIF to authorized vLEI issuers
  
- **LE** (Legal Entity) vLEI Credential
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-vLEI-credential.json>
  - Local: `Extension/Schemas/legal-entity-vLEI-credential.json`
  - Purpose: Issued by QVI to legal entities
  
- **OOR** (Official Organizational Role) Credential
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-official-organizational-role-vLEI-credential.json>
  - Local: `Extension/Schemas/legal-entity-official-organizational-role-vLEI-credential.json`
  - Purpose: Issued to individuals in official roles within legal entities
  
- **OOR AUTH** (OOR Authorization) Credential
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/oor-authorization-vlei-credential.json>
  - Local: `Extension/Schemas/oor-authorization-vlei-credential.json`
  - Purpose: Authorization from LE to QVI to issue OOR credentials
  
- **ECR** (Engagement Context Role) Credential
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-engagement-context-role-vLEI-credential.json>
  - Local: `Extension/Schemas/legal-entity-engagement-context-role-vLEI-credential.json`
  - Purpose: Issued for specific engagement contexts (e.g., supplier relationships)
  
- **ECR AUTH** (ECR Authorization) Credential
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/ecr-authorization-vlei-credential.json>
  - Local: `Extension/Schemas/ecr-authorization-vlei-credential.json`
  - Purpose: Authorization from LE to QVI to issue ECR credentials
  
- **iXBRL** (Verifiable iXBRL Report Attestation)
  - Schema: <https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/verifiable-ixbrl-report-attestation.json>
  - Local: `Extension/Schemas/verifiable-ixbrl-report-attestation.json`
  - Purpose: Attestation for XBRL financial reporting

## C#-TypeScript Interop Guidance

When working with C#-TypeScript interoperability in this project, keep in mind:

### Type Mapping Considerations

The signify-ts library built JavaScript sometimes conveys complex object structures rather than basic types:

- **Object Serialization**: Data is typically transmitted as nested JSON objects, not primitive types
- **Deep Nesting**: Expect multi-level object hierarchies that need proper, ordered deserialization on the C# side
- **Dynamic Properties**: Some objects may have dynamic or optional properties that require flexible C# models

### Best Practices

1. **Define DTOs (Data Transfer Objects)**
   - Create C# classes that mirror the TypeScript/JavaScript object structures
   - Use nullable types for optional properties
   - Consider using `JsonProperty` attributes for property name mapping

2. **Handle Nested Objects**
   - Use composition in C# models to match nested JavaScript objects
   - Consider using `JObject` or `dynamic` for highly variable structures
   - Implement proper null checking for nested properties

3. **Serialization/Deserialization**
   - Nested objects to and from signify-ts are order-dependent, which JSON serialization libraries often don't respect. Unless type is otherwise clear, consider using a Dictionary<string, object> type
   - Use `System.Text.Json` or `Newtonsoft.Json` consistently; however, typical use of these approaches might break required serialization order of nested objects that contain SAIDs (self addressing identifiers), which are based on hash function-generated digests, which often be included in nested CESR structures, such as ACDCs.
   - Configure serialization options to handle camelCase/PascalCase differences
   - Implement custom converters for complex type transformations

4. **Type Safety**
   - Validate incoming objects against expected schemas
   - Use TypeScript interfaces as reference for C# model definitions
   - Consider generating C# models from TypeScript definitions when possible
   - Create unit tests that detect issues with C# models used for interop, e.g. in the case where signify-ts provided types have changed

5. **Error Handling**
   - Implement robust error handling for deserialization failures
   - Log unexpected object structures for debugging
   - Provide meaningful error messages when type mismatches occur

### Use Signify-ts-shim.cs to wrap signify-ts
- Location: `Extension/Services/SignifyService/Signify-ts-shim.cs`
- Shim should handle common interaction patterns and potential issues
- For the signify-ts implementations that create network requests or wait for responses, these should implement timeouts and cancellation
- Default timeout: 30 seconds for network operations
- Use CancellationToken for all async operations
- Wrap all JavaScript interop calls in try-catch blocks with specific error messages

### JavaScript Interop with JsBind.Net

#### Preferred Approach Order
1. **JsBind.Net Library**: Use the JsBind.Net library for robust JavaScript interop without requiring new script files
2. **IJSRuntime with Existing APIs**: For basic JavaScript calls, use IJSRuntime.InvokeAsync<T>() with existing browser/DOM APIs
3. **TypeScript Modules**: Only create new TypeScript files when JsBind.Net and IJSRuntime cannot handle the requirement

#### Security-Compliant JavaScript Interop Patterns

**✅ CORRECT - Use TypeScript modules with import:**
```csharp
// Create a TypeScript module: wwwroot/scripts/es6/MyHelper.ts
var module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/MyHelper.js");
await module.InvokeVoidAsync("MyHelper.doSomething", parameter1, parameter2);
```

**✅ CORRECT - Use direct browser APIs:**
```csharp
// For simple browser API calls
var result = await _jsRuntime.InvokeAsync<bool>("window.matchMedia", "(prefers-color-scheme: dark)");
```

**❌ NEVER - Use eval() or dynamic code execution:**
```csharp
// SECURITY VIOLATION - Never do this!
await _jsRuntime.InvokeVoidAsync("eval", $"someCode({variable})");
await _jsRuntime.InvokeVoidAsync("eval", "function() { ... }");
await _jsRuntime.InvokeAsync<object>("eval", anyString);
```

**❌ NEVER - Use Function constructor or similar:**
```csharp
// SECURITY VIOLATION - Also forbidden!
await _jsRuntime.InvokeAsync<object>("Function", codeString);
await _jsRuntime.InvokeAsync<object>("new Function", parameters, body);
```

#### When Complex JavaScript Logic is Required
1. **Create TypeScript Module**: Write proper .ts file with exported functions
2. **Compile to ES6**: Let TypeScript compiler handle the conversion
3. **Import Module**: Use `import` to load the compiled .js module
4. **Invoke Methods**: Call specific exported functions with parameters

**Example TypeScript Module Pattern:**
```typescript
// wwwroot/scripts/es6/PortHelper.ts
export class PortHelper {
    static setupListener(port: chrome.runtime.Port, callback: any): void {
        port.onMessage.addListener(callback);
    }
}
```

```csharp
// C# usage
var portModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/PortHelper.js");
await portModule.InvokeVoidAsync("PortHelper.setupListener", portObject, dotNetCallback);
```

### Common Patterns

```csharp
// Example: Handling nested signify-ts objects
public class SignifyResponse
{
    public string Status { get; }
    public PayloadData Payload { get; }
    public Dictionary<string, object> Metadata { get; }
    
    public SignifyResponse(string status, PayloadData payload, Dictionary<string, object> metadata)
    {
        Status = status;
        Payload = payload;
        Metadata = metadata;
    }
}

public class PayloadData
{
    public string Id { get; }
    public List<NestedItem> Items { get; }
    // Use JObject for variable structures
    public JObject AdditionalData { get; }
    
    public PayloadData(string id, List<NestedItem> items, JObject additionalData)
    {
        Id = id;
        Items = items;
        AdditionalData = additionalData;
    }
}
```

## Common Gotchas and Solutions

### Issue: Temptation to use eval() for complex JavaScript interop
**Problem**: `IJSRuntime.InvokeVoidAsync("eval", ...)` violates CSP and security constraints
**Solution**: 
1. Create TypeScript module in `wwwroot/scripts/es6/`
2. Export static methods or classes
3. Import module: `await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/MyModule.js")`
4. Call methods: `await module.InvokeVoidAsync("MyClass.methodName", parameters)`

### Issue: JSON property order matters for CESR/SAID
**Solution**: Use `Dictionary<string, object>` with custom serializer that preserves insertion order, or use `LinkedHashMap` equivalent

### Issue: signify-ts returns undefined/null unexpectedly
**Solution**: Always use nullable types in C# DTOs and implement null-conditional operators (`?.`) in service layer

### Issue: Browser extension manifest version conflicts
**Solution**: Target Manifest V3 for Chrome/Edge

### Issue: Background worker state persistence across service worker lifecycle
**Problem**: Manifest V3 service workers can become inactive and restart, losing in-memory state stored in instance fields.
**Solution**:
1. **Prefer inferring state from existing persistent sources**: Check if state can be derived from port connections, registered content scripts, or other browser APIs rather than maintaining separate state.
   - Example: Instead of tracking injected tabs in a dictionary, check `_pageCsConnections` for active port connections (content scripts establish ports when loaded).
2. **If state must be persisted**: Use `chrome.storage.session` for session-scoped state that survives service worker restarts but clears when browser closes.
3. **For long-term persistence**: Use `chrome.storage.local` (but be mindful of storage limits and security).
4. **Best Practice**: Design the architecture to be stateless where possible, relying on browser APIs to query current state rather than caching it.

**Example - Checking for injected content scripts**:
```csharp
// ❌ BAD: Maintaining separate state that can be lost
private readonly ConcurrentDictionary<int, string> _injectedTabs = new();

// ✅ GOOD: Infer from existing port connections
var hasActiveConnection = _pageCsConnections.Values.Any(conn => conn.TabId == tab.Id);
```

## Security Constraints

The extension enforces strict security boundaries by design:

- **Passcode Caching**: Maximum 5 minutes of inactivity before automatic clearing
- **Content Script-initiated Messages**: Only accepted from active tab during/after authentication or after a signing association exists
- **HTTP Header Signing**:
  - Safe methods (GET) auto-approved
  - Unsafe methods (POST, PUT, DELETE) require explicit user consent
- **Script Execution**: No dynamic or inline scripts allowed (strict CSP)
  - **NEVER use eval() or any form of dynamic code evaluation**
    - `IJSRuntime.InvokeVoidAsync("eval", ...)` is FORBIDDEN
    - `IJSRuntime.InvokeAsync<object>("eval", ...)` is FORBIDDEN
    - `IJSRuntime.InvokeAsync<object>("Function", ...)` is FORBIDDEN
    - Use TypeScript modules with `import` instead
  - **NEVER use WebExtensions.Tabs.ExecuteScript() or chrome.tabs.executeScript()**
  - **NEVER inject JavaScript code into web pages programmatically**
  - All JavaScript must be in static files, no runtime code generation
  - Use JavaScript modules and imports for all interop needs
  - **Alternative to eval()**: Create TypeScript modules in `wwwroot/scripts/es6/` and import them
- **Data Isolation**: Sensitive data (passcode, private keys) must never reach content script or web page
- **Storage**: Use chrome.storage.local for non-sensitive data only
- **Permissions**: Declare minimum required and optional permissions in the extension's manifest
- **KERIA Communication**: All KERIA agent communications via authenticated signify-ts

### Design Security Principles

The extension follows these security principles:
- Only sends signed HTTP Header Requests to websites if they are safe (e.g., GET) or user approves them (e.g., POST)
- Caches the passcode only temporarily and clears it from cache after a maximum of 5 minutes of inactivity
- Only accepts content script messages from the active tab website, during authentication or after a signing association exists
- Declares minimum required and optional permissions in the extension's manifest
- Never runs dynamic or inline scripts
- Assures all sensitive data (e.g., passcode) never reaches the content script or website

## Priority Order for Code Changes

When making changes, prioritize in this order:
1. **Security** - Never expose keys, secrets, or sensitive data
2. **Functionality** - Ensure core KERI/ACDC operations work correctly
3. **Type Safety** - Maintain strong typing across C#/TypeScript boundary
4. **Performance** - Optimize after functionality is verified
5. **Code Style** - Apply formatting rules last

## Testing Strategy

### Test Approach

**Unit Tests** (xUnit with Moq):
- **Why**: Fast feedback, isolate component behavior
- **Pattern**: Arrange-Act-Assert with descriptive test names
- **Mocking**: Use Moq for JavaScript interop (`IJSRuntime`) to avoid browser dependencies

**Integration Tests**:
- **Why**: Verify signify-ts interop with actual KERIA output
- **Critical**: Test with real signify-ts responses to catch type mismatches early
- **Focus**: End-to-end flows that cross C#-JavaScript boundary

**Manual Browser Testing**:
- **Why required**: Extension UI/UX, service worker behavior, and chrome.* APIs cannot be fully automated
- **When**: After significant changes to BackgroundWorker, ContentScript, or UI flows

### What to Test (Priority Order)

1. **CESR/SAID Ordering** (Critical):
   - **Why**: Field order determines cryptographic hashes; reordering breaks signatures
   - **How**: Verify `RecursiveDictionary` preserves insertion order through C# → JS boundary
   - **Test data**: Use local vLEI schemas in `Extension/Schemas/`

2. **JavaScript Interop** (High):
   - **Why**: Type mismatches between C# and signify-ts cause runtime failures
   - **What**: Mock `IJSRuntime` calls, test timeout/cancellation, verify RecursiveDictionary → JSObject conversion
   - **Edge cases**: null/undefined, empty objects, deeply nested structures

3. **Message Routing & Security Boundaries** (High):
   - **Why**: Security constraints prevent sensitive data from reaching content script
   - **What**: Validate message handlers respect 4-layer boundary system
   - **Focus**: Ensure passcode never leaves BackgroundWorker context

4. **Storage Service** (Medium):
   - **Why**: chrome.storage (.local and .sync) is primary persistence mechanism
   - **What**: Test serialization/deserialization, storage limits, concurrent access

5. **Credential Schema Validation** (Medium):
   - **Why**: Catch schema drift from vLEI spec updates
   - **Test data**: Use local schema files, update when GLEIF releases new versions

### Testing Anti-Patterns

**Don't**:
- Serialize credentials with System.Text.Json or Newtonsoft.Json (breaks CESR/SAID)
- Mock BackgroundWorker service worker events (test actual chrome.* APIs or use integration tests)
- Test browser caching behavior (document it instead)

**Command reference**: See xUnit documentation for test filtering syntax.

## Browser Extension Configuration

### Manifest Configuration (manifest.json)
- **Manifest Version**: V3 (required for Chrome Web Store)
- **Compatibility**: Chrome, Edge, Brave (Chromium-based browsers minimum version as in manifest.json)
- **Permissions**:
  - Minimum required permissions declared
  - Host permissions requested at runtime when needed
- **Content Security Policy**:
  - Strict CSP preventing inline scripts
  - No eval() or dynamic code execution
  - Trusted sources explicitly listed

### Extension Components
- **Background Worker**: BackgroundWorker.cs and supporting JS interop
- **Content Script**: Injected on-demand, not automatically
- **Blazor WASM UI**:
  - **Action Popup**: Primarily for tab-specific interactions, but may include general features such as Unlock
  - **Options Page**: Full tab for general features only
  - **Extension Tab**: Full tab for general features only
  - **SidePanel**: (future) Tab-specific interactions or general features

### Build Output Structure

Final extension package: `Extension/bin/{Debug|Release}/net9.0/browserextension/`
- Contains manifest.json, Blazor WASM runtime (_framework/), and JavaScript bundles (scripts/)
- Load this directory as unpacked extension in browser

## Build System Architecture (For Maintainers)

### Key Files

1. **Extension.csproj** - MSBuild targets and properties
   - `BuildExtensionScripts` target runs `npm run build` in scripts/ workspace
   - `BuildAppTs` target compiles app.ts
   - ProjectReferences to .esproj files ensure correct build order

2. **scripts/package.json** - npm workspaces root
   - `build` - Builds all three TypeScript projects in order (types → modules → bundles)
   - Coordinates @keriauth/types, @keriauth/modules, @keriauth/bundles

3. **scripts/bundles/esbuild.config.js** - JavaScript bundler configuration
   - Bundles signify-ts and dependencies
   - Alias plugins resolve @keriauth/types to compiled output
   - Platform-specific polyfills for libsodium

4. **scripts/types/tsconfig.json**, **scripts/modules/tsconfig.json** - TypeScript configs
   - ES6 module output to Extension/wwwroot/scripts/es6/
   - Project references for build ordering

### Build Flags

| Flag | Effect | Set By |
|------|--------|--------|
| `BuildingProject=true` | Enable TypeScript build | `-p:FullBuild=true` |
| `SkipJavaScriptBuild=true` | Skip TypeScript | `-p:Quick=true` |
| `Configuration=Release` | Release mode | `-p:Production=true` |
| `DesignTimeBuild=true` | Skip npm (IDE intellisense) | Visual Studio |

### Output Directories

**Why directory structure matters**:

1. **`Extension/wwwroot/scripts/`** - TypeScript compilation output
   - `es6/` - ES6 modules from tsc
   - `esbuild/` - Bundled scripts (signifyClient.js, demo1.js must exist before C# build)
   - These become StaticWebAssets consumed by Blazor build

2. **`Extension/bin/Debug/net9.0/browserextension/`** - Final extension package
   - Contains BackgroundWorker.js (generated, imports scripts from wwwroot/)
   - Ready to load as unpacked extension

**Critical**: BackgroundWorker.js generator requires JavaScript files in wwwroot/scripts/esbuild/ to exist before MSBuild runs. This is why TypeScript must build first.

### Manual Testing Approach

**After building**, test the extension in the browser:
1. Load unpacked extension from build output directory
2. Verify manifest loads (check for console errors)
3. Test core functionality (create/unlock identifier, sign requests)
4. Check service worker logs for runtime errors

**Why manual testing matters**: Browser extension behavior (service worker lifecycle, chrome.* API interactions, UI rendering) cannot be fully replicated in automated tests.

## Dependency Management

### C# Dependencies (NuGet)
Managed in `.csproj` files:
- **MudBlazor**: UI component library
- **Microsoft.AspNetCore.Components.WebAssembly**: Blazor WASM framework
- **Newtonsoft.Json**: JSON serialization (consider System.Text.Json for new code)
- **Polly**: Resilience and transient fault handling
- **xUnit/Moq**: Testing frameworks

### JavaScript Dependencies (npm)
Managed in `scripts/bundles/package.json`:
- **signify-ts**: For KERI operations
- **signify-polaris-web**: Pinned to commit `7d7dd13` for web page protocol
- **libsodium-wrappers-sumo**: Cryptographic operations
- **esbuild**: JavaScript bundler for extension scripts

Managed in `scripts/` workspace (types, modules, bundles):
- **TypeScript**: Version 5.4.2 for type safety
- **chrome-types**: Chrome extension API type definitions

### Version Management
- **C# packages**: Use exact versions or narrow ranges
- **npm packages**: Pin critical dependencies (signify-ts, polaris-web) to specific commits
- **Node.js**: Requires >= 22.13.0 (specified in package.json engines)
- **.NET SDK**: 9.0

## Cross-Platform Build Issues & File Lock Detection

### Windows/WSL Compatibility

This project supports both Windows and WSL environments, but file locks and path conflicts can occur when:
- Visual Studio is open while building in WSL
- Windows Explorer is browsing the project directory during WSL builds
- WSL processes are accessing files during Windows builds
- NuGet package paths become mixed between Windows (`C:\Users\...`) and WSL (`/home/...`) formats

### Troubleshooting Build Issues

#### NuGet Restore Loop Error
**Symptoms**: "A NuGet restore loop has been detected" in Visual Studio
**Cause**: npm build targets running during restore operations
**Solution**: 
1. Close Visual Studio and any Windows Explorer windows
2. Clean and rebuild from WSL: `rm -rf Extension/obj Extension.Tests/obj && dotnet restore && dotnet build`

#### Package Not Found Errors
**Symptoms**: "Package Blazor.BrowserExtension.Build, version X.X.X was not found"
**Cause**: Path mismatch between Windows and WSL NuGet package locations
**Solution**:
1. Close Visual Studio completely
2. Close Windows Explorer if browsing project directory
3. Clean obj directories: `rm -rf Extension/obj Extension.Tests/obj`
4. Restore with correct paths: `dotnet restore`
5. Build: `dotnet build /p:BuildingProject=true`

#### TypeScript Permission Errors
**Symptoms**: "tsc: Permission denied" during esbuild type checking
**Cause**: File permissions or locks from Windows processes
**Solution**:
1. Close Visual Studio and Windows Explorer
2. Run build with skip flag: `dotnet build /p:SkipJavaScriptBuild=true`
3. Manually build TypeScript: `cd Extension && npm run build`

### Build Environment Best Practices

#### For Windows Development:
- Use Visual Studio for C# development and debugging
- Build final release using `dotnet build` in Command Prompt/PowerShell
- Avoid mixing WSL commands while Visual Studio is open

#### For WSL Development:
- Ensure Visual Studio and Windows Explorer are closed before building
- Use `dotnet build /p:BuildingProject=true` for full builds
- Monitor for Windows path pollution in `Extension/obj/*.props` files

#### Cross-Environment Workflow:
1. **Code in Windows**: Use Visual Studio for C# editing, IntelliSense
2. **Build in WSL**: Close VS, run builds in WSL for consistency
3. **Test in both**: Verify extension works in both environments

### Emergency Build Recovery

If builds consistently fail with path/lock issues:

```bash
# Full cleanup and rebuild
rm -rf Extension/bin Extension/obj Extension.Tests/obj
rm -rf scripts/types/dist scripts/modules/dist scripts/bundles/node_modules/.cache
dotnet clean
dotnet restore --force-evaluate
cd scripts && npm run build && cd ..
dotnet build -p:Quick=true
```

### Build Command Reference

| Command | Environment | Purpose | Notes |
|---------|-------------|---------|-------|
| `dotnet build -p:FullBuild=true` | **Any** | **Full build (shortcut)** | **Easiest to type** |
| `dotnet build -p:Production=true` | **Any** | **Production build (shortcut)** | **Release + Full build** |
| `dotnet build -p:Quick=true` | **Any** | **Quick build (shortcut)** | **Skip TypeScript if needed** |
| `dotnet build /p:BuildingProject=true` | **Any** | **Canonical full build** | **Recommended for all environments** |
| `dotnet build --configuration Release /p:BuildingProject=true` | **Any** | **Production build (canonical)** | **Used by CI/CD** |
| `dotnet build` | Any | C# only build | No TypeScript compilation |
| `cd scripts && npm run build` | Any | TypeScript only | Build all TS projects |
| `dotnet clean` | Any | Clean .NET outputs | Safe for both environments |

## Debugging Tips

### General Debugging
- Browser extension logs: Check browser console (F12) and extension service worker logs
- Blazor WASM debugging: Enable detailed errors in `Program.cs` for development
- signify-ts issues: Use `console.log`/`console.debug` in TypeScript, visible in browser console
- Network issues: Check CORS policies and content security policy (CSP) settings
- TypeScript compilation issues: Check `tsconfig.json` for module resolution settings
- ESLint issues: Run `npx eslint --fix` to auto-fix style issues

### Debugging Blazor Code
1. Launch extension in browser
2. Open extension popup
3. Right-click → Inspect
4. DevTools opens
5. Sources tab → see Blazor C# code

**Advanced Debugging Setup:**
For more details on running and debugging Blazor browser extensions, see the [Blazor.BrowserExtension documentation](https://mingyaulee.github.io/Blazor.BrowserExtension/running-and-debugging).

### IDE-Specific Considerations

**VS Code**:
- Build tasks available via `Ctrl+Shift+B` (defined in `.vscode/tasks.json`)
- Debugging: Press F5 to launch extension in new browser window
- Terminal uses WSL on Windows (configured in `.vscode/settings.json`)

**Visual Studio**:
- **Critical**: Does NOT auto-rebuild TypeScript - must manually run npm build after .ts changes
- **File locking**: Can prevent WSL builds - use VS **OR** WSL, not both simultaneously
- **Debugging**: Browser DevTools works better than VS debugger for Blazor WASM extension code
- Close VS Code and WSL terminals before building in Visual Studio

**Claude Code (WSL/Linux)**:
- Handles builds automatically, no special configuration needed
- If build fails: Close Visual Studio (Windows) and Windows Explorer to release file locks

**Key difference**: Visual Studio requires manual TypeScript compilation; VS Code and Claude Code can automate it.

## Common Build Issues and Solutions

### Issue: BackgroundWorker.js Missing TypeScript Modules (Two-Build Problem)

**Symptom:** After running `npm run clean`, the first `dotnet build` succeeds but BackgroundWorker.js doesn't include signifyClient.js or demo1.js imports. The second build includes them correctly.

**Root Cause:** The Blazor.BrowserExtension BackgroundWorker.js generator runs during `StaticWebAssetsPrepareForRun`, which executes BEFORE the `BuildExtensionScripts` MSBuild target. On a clean build, the generator scans for static assets before TypeScript compilation completes, so it doesn't find the JS files.

**Why Second Build Works:** The JS files from the first build persist (not cleaned by `dotnet clean`), so the second build's BackgroundWorker generator finds them.

**Solution Option 1: Recommended Workflow**

```bash
# Build TypeScript FIRST (in scripts workspace)
cd scripts && npm run build

# Then build C# (Quick mode since TypeScript already built)
cd .. && dotnet build -p:Quick=true
```

**Solution Option 2: Accept Two-Build After Deep Clean**

```bash
# After npm run clean, run build twice
dotnet build -p:FullBuild=true  # First build: creates JS files
dotnet build -p:FullBuild=true  # Second build: includes them in BackgroundWorker.js
```

**Solution Option 3: Don't Clean TypeScript Between Builds**

```bash
# Normal workflow (JS files persist across builds)
dotnet build -p:FullBuild=true  # Works correctly

# Deep clean only when absolutely necessary (removes compiled JS)
cd Extension && npm run clean  # Then use Solution 1 or 2
```

**Note:** The csproj preserves `wwwroot/scripts/**/*.js` files during `dotnet clean`. Only use `npm run clean` when you need to regenerate all TypeScript outputs from scratch.

**Verification:**

```bash
# Check if BackgroundWorker.js includes your modules
grep -c "signifyClient" Extension/bin/Debug/net9.0/browserextension/content/BackgroundWorker.js
# Should return 2 (one import, one in allImports array)
```

### Issue: "SignifyClient not connected" at Runtime

**Cause:** TypeScript changes not compiled or extension not reloaded

**Solution:**

```bash
# 1. Rebuild TypeScript
cd scripts && npm run build

# 2. Rebuild extension package
cd .. && dotnet build -p:Quick=true

# 3. Reload extension in browser
# Go to chrome://extensions → Click reload button
```

### Issue: npm Build Fails with "Permission Denied"

**Cause:** Windows process (Visual Studio/Explorer) has files locked

**Solution:**

```bash
# 1. Close Visual Studio
# 2. Close Windows Explorer windows showing project
# 3. Try build again
cd scripts && npm run build
```

### Issue: Build Works in VS Code but Not Visual Studio

**Cause:** Different build targets triggered

**Solution in Visual Studio:**

```powershell
# Use Developer PowerShell or Package Manager Console
dotnet build -p:FullBuild=true
```

Then use "Rebuild Solution" (not just "Build").

### Issue: "NuGet restore loop detected"

**Cause:** Build targets running during restore operation

**Solution:**

```bash
# 1. Close Visual Studio
# 2. Delete obj directories
rm -rf Extension/obj Extension.Tests/obj

# 3. Restore and build separately
dotnet restore
dotnet build -p:FullBuild=true
```
