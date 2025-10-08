# TODO: Update CLAUDE.md

When this refactoring is complete, update CLAUDE.md with the following changes:

## Architecture Changes

### JavaScript Interop Binding Layer

Document the new binding architecture in the **Architecture Overview** section:

**New Section: JavaScript Interop Architecture**
```markdown
### JavaScript Interop Architecture

The extension uses a layered approach for JavaScript interop:

1. **JsModuleLoader** (`Extension/Services/JsBindings/JsModuleLoader.cs`)
   - Centralized service for loading JavaScript ES modules
   - Fail-fast pattern: loads all modules at startup in `Program.cs`
   - Provides strongly-typed module access via `GetModule(moduleName)`
   - Loaded modules:
     - `signifyClient` - bundled signify-ts client (ESM)
     - `storageHelper` - chrome.storage helpers (ES6)
     - `permissionsHelper` - chrome.permissions helpers (ES6)
     - `portMessageHelper` - port messaging utilities (ES6)
     - `swAppInterop` - service worker/app communication (ES6)
     - `webauthnCredentialWithPRF` - WebAuthn operations (ES6)

2. **Binding Classes** (`Extension/Services/JsBindings/`)
   - `ISignifyClientBinding` / `SignifyClientBinding`
     - Strongly-typed wrapper around signify-ts JavaScript module
     - All methods return `ValueTask<T>` for async operations
     - Registered as singleton: `ISignifyClientBinding` → `SignifyClientBinding`
   - Pattern for future bindings:
     ```csharp
     public interface IMyBinding {
         ValueTask<string> MyMethodAsync(string param, CancellationToken ct = default);
     }

     [SupportedOSPlatform("browser")]
     public class MyBinding(IJsModuleLoader moduleLoader) : IMyBinding {
         private IJSObjectReference Module => moduleLoader.GetModule("myModule");

         public ValueTask<string> MyMethodAsync(string param, CancellationToken ct = default) =>
             Module.InvokeAsync<string>("myMethod", ct, param);
     }
     ```

3. **Service Layer**
   - Services inject binding interfaces (not concrete classes)
   - Example: `SignifyClientService` injects `ISignifyClientBinding`
   - Services handle timeout logic via `TimeoutHelper.WithTimeout()`
   - Services perform JSON deserialization and model mapping
```

### Module Loading at Startup

Update the **Application Setup** section:

**Add to Dependency Injection subsection:**
```markdown
#### Module Loading Pattern
JavaScript modules are loaded during application startup in `Program.cs`:

```csharp
// After building WebAssemblyHost but before running it
var moduleLoader = host.Services.GetRequiredService<IJsModuleLoader>();
await moduleLoader.LoadAllModulesAsync();  // Fail-fast: throws if any module fails
await host.RunAsync();
```

All modules are loaded upfront to:
- Detect module loading issues immediately at startup
- Avoid lazy-loading race conditions
- Provide clear error messages if modules are missing
```

### TypeScript Module Organization

Update the **Key project layout** section:

**Replace the `Extension/wwwroot/scripts/` section with:**
```markdown
- `Extension/wwwroot/scripts/` - TypeScript/JavaScript code
  - `es6/` - TypeScript modules compiled to ES6 (for C# import via IJSRuntime)
    - `storageHelper.ts` - chrome.storage API helpers
    - `PermissionsHelper.ts` - chrome.permissions API helpers
    - `PortMessageHelper.ts` - Port messaging utilities
    - `SwAppInterop.ts` - Service Worker/App communication
    - `webauthnCredentialWithPRF.ts` - WebAuthn with PRF extension
  - `esbuild/` - Bundled scripts (IIFE or ESM depending on use)
    - `signifyClient.ts` - signify-ts wrapper (bundled as ESM for C# interop)
    - `ContentScript.ts` - Content script (bundled as IIFE for injection)
  - `types/` - TypeScript type definitions
```

### Removed Components

Add a new **Deprecated/Removed Components** section:

```markdown
## Deprecated/Removed Components

The following components were removed during the refactoring:

- **SignifyClientShim.cs** - Removed in favor of direct `ISignifyClientBinding` usage
  - Clients now inject `ISignifyClientBinding` instead of `SignifyClientShim`
  - All shim methods were wrappers that added no value over the binding

- **MainWorldContentScript.ts** - Removed as unused placeholder
  - Was a placeholder for potential future MAIN world API injection
  - Current architecture uses polaris-web protocol via `window.postMessage()`
  - If MAIN world API is needed in future, create new implementation

- **JSHost.ImportAsync() in Program.cs** - Removed legacy module loading
  - Was causing path resolution issues (`/framework/wwwroot/` vs `/wwwroot/`)
  - Replaced by `JsModuleLoader` centralized loading service
  - No code uses `[JSImport]` attributes - all interop via `IJSRuntime`
```

### Build Changes

Update the **Build & Test Commands** section:

**Add to Frontend Build Commands:**
```markdown
#### esbuild Configuration
Module bundling configured in `Extension/esbuild.config.js`:
- **signifyClient**: ESM bundle with signify-ts dependencies
- **ContentScript**: IIFE bundle for browser injection
- Type checking runs before production builds
- Watch mode available: `npm run watch:esbuild`
```

## Code Patterns

Add a new **Common Patterns** section after **C# Coding Guidelines**:

```markdown
## Common Patterns

### Using Signify Client Binding

**Correct Pattern:**
```csharp
public class MyService(ISignifyClientBinding signifyClient) {
    private readonly ISignifyClientBinding _signifyClient = signifyClient;

    public async Task<Result<State>> GetStateAsync() {
        try {
            var jsonString = await _signifyClient.GetStateAsync();
            var state = JsonSerializer.Deserialize<State>(jsonString);
            return Result.Ok(state);
        }
        catch (Exception e) {
            return Result.Fail<State>($"Failed to get state: {e.Message}");
        }
    }
}
```

**Incorrect (Old Pattern):**
```csharp
// ❌ Don't use SignifyClientShim (removed)
public class MyService(SignifyClientShim shim) {
    private readonly SignifyClientShim _shim = shim;
}
```

### Timeout Handling

The `TimeoutHelper` supports both `Task<T>` and `ValueTask<T>`:

```csharp
// With ValueTask (from bindings)
var result = await TimeoutHelper.WithTimeout<string>(
    ct => _signifyClient.ConnectAsync(url, passcode, ct),
    TimeSpan.FromSeconds(30)
);

// With Task
var result = await TimeoutHelper.WithTimeout<string>(
    ct => SomeTaskReturningMethodAsync(ct),
    TimeSpan.FromSeconds(30)
);
```

### JavaScript Module Loading

**At Startup (Program.cs):**
```csharp
var moduleLoader = host.Services.GetRequiredService<IJsModuleLoader>();
await moduleLoader.LoadAllModulesAsync();  // Loads all configured modules
```

**In Services (via DI):**
```csharp
// Don't call LoadAllModulesAsync() again in services
// Modules are already loaded at startup
public class MyService(IJsModuleLoader moduleLoader) {
    private IJSObjectReference Module => moduleLoader.GetModule("myModule");

    public async Task DoSomethingAsync() {
        await Module.InvokeVoidAsync("myJsFunction");
    }
}
```

### Creating New JavaScript Bindings

1. Create interface in `Extension/Services/JsBindings/`:
```csharp
public interface IMyBinding {
    ValueTask<string> MyOperationAsync(string param, CancellationToken ct = default);
}
```

2. Implement binding class:
```csharp
[SupportedOSPlatform("browser")]
public class MyBinding(IJsModuleLoader moduleLoader) : IMyBinding {
    private IJSObjectReference Module => moduleLoader.GetModule("myModule");

    public ValueTask<string> MyOperationAsync(string param, CancellationToken ct = default) =>
        Module.InvokeAsync<string>("myOperation", ct, param);
}
```

3. Register in `Program.cs`:
```csharp
builder.Services.AddSingleton<IMyBinding, MyBinding>();
```

4. Add module to `JsModuleLoader` configuration:
```csharp
// In JsModuleLoader constructor or configuration
_moduleConfigurations["myModule"] = new ModuleConfig {
    ModulePath = "/scripts/es6/myModule.js",
    IsRequired = true
};
```
```

## Update References

Search and update the following references in CLAUDE.md:

1. **Find:** `SignifyClientShim`
   - **Replace with:** Direct reference to `ISignifyClientBinding` usage pattern

2. **Find:** References to `signify_ts_shim.ts`
   - **Update context:** Now bundled as `signifyClient.ts` via esbuild

3. **Find:** Module loading patterns
   - **Update:** Show `JsModuleLoader` pattern instead of individual `import()` calls

4. **Find:** `wwwroot/scripts/` directory structure
   - **Update:** With new es6/ and esbuild/ organization

## Known Issues (Post-Refactoring)

### Issue 1: Module Not Found When BackgroundWorker Handles CS Messages

**Status:** ⚠️ UNRESOLVED - Root cause identified, fix needed

**Symptom:**
When a web page sends a `/signify/sign-request` message to the Content Script, which forwards it via `port.postMessage` to the BackgroundWorker, the following error occurs:

```
info: Extension.BackgroundWorker[0]
      BW HandleSignRequest: {"type":"/signify/sign-request","requestId":"d6825ed0-2611-49b6-9b16-8255d7c5f4aa","payload":{"url":"http://localhost:5173/","method":"GET"}}
info: Extension.BackgroundWorker[0]
      BW HandleSignRequest: tabId: 1716398967, origin: http://localhost:5173
info: Extension.BackgroundWorker[0]
      Blazor App port disconnected: BA_TAB|c8b6b56c-b702b279-afd437fc-698741fd
info: Extension.BackgroundWorker[0]
      Port disconnected: BA_TAB|c8b6b56c-b702b279-afd437fc-698741fd
warn: Extension.Services.SignifyService.SignifyClientService[0]
      GetIdentifiers: Exception: Microsoft.JSInterop.JSException: Could not find 'getAIDs' ('getAIDs' was undefined).
      Error: Could not find 'getAIDs' ('getAIDs' was undefined).
```

**Root Cause - IDENTIFIED:**

Blazor.BrowserExtension creates **separate Blazor WASM runtime instances** for each context:
- **Popup/SPA Context**: Has its own `IJSRuntime` instance
- **BackgroundWorker Context**: Has its own `IJSRuntime` instance (service worker)

**Current Behavior:**
1. `Program.cs` calls `moduleLoader.LoadAllModulesAsync()` - this runs ONLY in Popup/SPA runtime
2. BackgroundWorker has NO module loading in its `Main()` method
3. When BackgroundWorker tries to use `SignifyClientBinding`, the module doesn't exist in BackgroundWorker's runtime
4. Error: "Could not find 'getAIDs'" because module was never loaded in BackgroundWorker's `IJSRuntime`

**This is NOT a timing issue - it's a CONTEXT issue:**
- Modules ARE successfully loaded... but only in Popup/SPA's runtime
- BackgroundWorker's runtime never loads any modules
- Singleton services exist in BOTH runtimes but have separate state per runtime
- `JsModuleLoader._isInitialized` is `true` in Popup but `false` in BackgroundWorker

**Solution:**
BackgroundWorker needs to load modules in its own runtime context. See Issue 2 below.

**Related Files:**
- [Extension/Program.cs](Extension/Program.cs:62-64) - Loads modules only for Popup/SPA runtime
- [Extension/BackgroundWorker.cs](Extension/BackgroundWorker.cs:80-104) - `Main()` has no module loading
- [Extension/Services/JsBindings/JsModuleLoader.cs](Extension/Services/JsBindings/JsModuleLoader.cs) - Singleton but separate state per runtime

---

### Issue 2: BackgroundWorker Missing Module Loading

**Status:** ⚠️ ARCHITECTURAL - Needs implementation

**Problem:**
BackgroundWorker.Main() does NOT load JavaScript modules, but services injected into BackgroundWorker depend on modules.

**Current Code (BackgroundWorker.cs:82-83):**
```csharp
[BackgroundWorkerMain]
public override void Main() {
    // JavaScript module imports are handled in app.ts afterStarted() hook
    // which is called after Blazor is fully initialized and ready
```

**Reality:**
- ❌ Comment is INCORRECT - `app.ts:afterStarted()` does NOT load modules (verified line 159-162)
- ❌ BackgroundWorker never calls `LoadAllModulesAsync()`
- ❌ BackgroundWorker's `IJSRuntime` has NO modules

**Solution Required:**
Add module loading to BackgroundWorker startup:
```csharp
[BackgroundWorkerMain]
public override async void Main() {
    // Load JavaScript modules in BackgroundWorker's runtime context
    var moduleLoader = /* get from DI or services */;
    await moduleLoader.LoadAllModulesAsync();

    // Then register event listeners...
    WebExtensions.Runtime.OnInstalled.AddListener(OnInstalledAsync);
    // etc...
}
```

**Challenge:**
- `Main()` is `void`, not `async` - need to handle async module loading
- May need to use Blazor.BrowserExtension specific patterns for BackgroundWorker initialization

**Related Files:**
- [Extension/BackgroundWorker.cs](Extension/BackgroundWorker.cs:80-104)
- [Extension/wwwroot/app.ts](Extension/wwwroot/app.ts:159-162)

---

### Issue 3: Duplicate Module Loading in Services

**Status:** ⚠️ CODE SMELL - Should be refactored

**Problem:**
Three services perform lazy module loading for modules that `JsModuleLoader` already loads eagerly:

1. **WebauthnService** (Extension/Services/WebauthnService.cs:20)
   ```csharp
   interopModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
       "import", "/scripts/es6/webauthnCredentialWithPRF.js"
   );
   ```
   - Duplicates: `JsModuleLoader` loads same module as "webauthnCredentialWithPRF"

2. **StorageService** (Extension/Services/StorageService.cs:53)
   ```csharp
   IJSObjectReference _module = await jsRuntime.InvokeAsync<IJSObjectReference>(
       "import", "/scripts/es6/storageHelper.js"
   );
   ```
   - Duplicates: `JsModuleLoader` loads same module as "storageHelper"

3. **AppBwMessagingService** (Extension/Services/AppBwMessagingService.cs:19)
   ```csharp
   _interopModule = await jsRuntime.InvokeAsync<IJSObjectReference>(
       "import", "./scripts/es6/SwAppInterop.js"
   );
   ```
   - Duplicates: `JsModuleLoader` loads same module as "swAppInterop"
   - **Path inconsistency**: Uses `./scripts/` (relative) instead of `/scripts/` (absolute)

**Issues:**
- Wasteful: Each module imported twice (once by JsModuleLoader, once by service)
- Inconsistent: Some services use JsModuleLoader, others use direct import
- Race condition potential: If service initializes before Program.cs module loading completes
- Path confusion: Mix of relative and absolute paths

**Solution:**
Refactor services to use `IJsModuleLoader` instead of lazy importing:
```csharp
// Current (BAD):
public class WebauthnService(IJSRuntime jsRuntime) {
    private IJSObjectReference? interopModule;

    private async Task Initialize() {
        interopModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "/scripts/es6/webauthnCredentialWithPRF.js"
        );
    }
}

// Proposed (GOOD):
public class WebauthnService(IJsModuleLoader moduleLoader) {
    private IJSObjectReference Module => moduleLoader.GetModule("webauthnCredentialWithPRF");

    // No Initialize() needed - module already loaded at startup
}
```

**Benefits:**
- Single source of truth for module loading
- Fail-fast at startup if modules missing
- Consistent pattern across all services
- No race conditions

**Related Files:**
- [Extension/Services/WebauthnService.cs](Extension/Services/WebauthnService.cs:16-31)
- [Extension/Services/StorageService.cs](Extension/Services/StorageService.cs:43-66)
- [Extension/Services/AppBwMessagingService.cs](Extension/Services/AppBwMessagingService.cs:14-25)

---

### Issue 4: app.ts Comment Misleading

**Status:** ⚠️ DOCUMENTATION - Comment correction needed

**Problem:**
BackgroundWorker.cs line 82-83 comment claims:
```csharp
// JavaScript module imports are handled in app.ts afterStarted() hook
// which is called after Blazor is fully initialized and ready
```

**Reality:**
`app.ts:afterStarted()` implementation (line 159-162):
```typescript
export function afterStarted(blazor: unknown): void {
    console.log('app.ts: afterStarted - Blazor runtime ready');
    // Note: Module imports are handled in Program.cs using JSHost.ImportAsync()
}
```

**Facts:**
- ❌ `app.ts:afterStarted()` does NOT load any modules
- ❌ Comment reference to `JSHost.ImportAsync()` is OUTDATED (we removed that code)
- ✅ Modules ARE loaded in `Program.cs` via `JsModuleLoader.LoadAllModulesAsync()`
- ✅ But ONLY in Popup/SPA runtime, NOT in BackgroundWorker runtime

**Solution:**
Update comments in both files to reflect current architecture:
- BackgroundWorker.cs: Remove misleading comment
- app.ts: Update comment to reference `JsModuleLoader` in Program.cs

**Related Files:**
- [Extension/BackgroundWorker.cs](Extension/BackgroundWorker.cs:82-83)
- [Extension/wwwroot/app.ts](Extension/wwwroot/app.ts:161)

---

## Fix Priority (To Be Determined)

The issues should be addressed in a specific order to avoid breaking existing functionality:

**Suggested Order:**
1. **Issue 2** - Add module loading to BackgroundWorker (fixes Issue 1)
2. **Issue 3** - Refactor services to use IJsModuleLoader consistently
3. **Issue 4** - Update misleading comments

**Alternative Order (if Issue 2 is complex):**
1. **Issue 3** - Refactor services first (simpler, improves consistency)
2. **Issue 2** - Add BackgroundWorker module loading (harder, requires async pattern)
3. **Issue 4** - Update comments last

**Note:** Actual fix order to be determined based on complexity and risk assessment

---

## Verification Checklist

Before marking CLAUDE.md as updated, verify:

- [ ] All references to `SignifyClientShim` removed or updated
- [ ] `JsModuleLoader` architecture documented
- [ ] `ISignifyClientBinding` usage pattern documented
- [ ] Module organization (es6/ vs esbuild/) clearly explained
- [ ] Removed components listed in deprecation section
- [ ] Build command section includes esbuild information
- [ ] Code examples use current patterns (not deprecated ones)
- [ ] TimeoutHelper ValueTask support documented
- [ ] Known issue documented (module access in BackgroundWorker context)

---

## Summary of Changes for CLAUDE.md

### New Sections to Add:
- JavaScript Interop Architecture (in Architecture Overview)
- Deprecated/Removed Components
- Common Patterns (JavaScript interop examples)

### Existing Sections to Update:
- Architecture Overview (module organization)
- Application Setup (DI and module loading)
- Build & Test Commands (esbuild details)
- Common Gotchas (update JSHost warning)

### Code Examples to Update:
- Replace SignifyClientShim examples with ISignifyClientBinding
- Add TimeoutHelper ValueTask examples
- Add JsModuleLoader usage examples
