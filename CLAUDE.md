# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architecture Overview

This is a KERI Auth browser extension built with:
- Blazor WebAssembly (.NET 9.0) for the extension UI
- TypeScript for content scripts and service worker
- MudBlazor for UI components
- signify-ts for KERI/ACDC operations
- polaris-web for web page communication protocol

Key project layout:
- `Extension/` - Main browser extension project (Blazor WASM)
- `Extension.Tests/` - xUnit test project
- `Extension/Services/` - Core services including SignifyService for KERI operations
- `Extension/UI/` - Blazor pages and components
- `Extension/wwwroot/scripts/` - TypeScript/JavaScript code
  - `es6/` - TypeScript modules compiled to ES6
  - `esbuild/` - Bundled scripts for service worker and content script
- `Extension/Schemas/` - vLEI credential schema definitions (local copies)

The extension follows a multi-component architecture:
1. **Service Worker** - Background script handling extension lifecycle and message routing
2. **Content Script** - Injected into web pages, bridges page and extension communication
3. **Blazor WASM App** - UI layer for extension popup and tabs
4. **SignifyService** - Manages KERI/ACDC operations via signify-ts JavaScript interop

Services use dependency injection with primarily singleton lifetime for state management across the extension. The StorageService provides persistent storage using chrome.storage API.

## Message Flow Architecture

The extension uses a multi-layered messaging system with strict boundaries:

1. **Web Page ↔ Content Script**: Via polaris-web JavaScript API protocol
   - Web page calls polaris-web methods
   - Content script validates and forwards to service worker

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

## Development Environment

Primary development environment is Windows running WSL2. Commands work in both Windows PowerShell/Command Prompt and WSL2 bash. Claude Code operates within the WSL2 environment.

## Prerequisites

- Node.js >= 22.13.0
- npm >= 11.0.0  
- .NET 9.0 SDK
- Chromium-based browser version 127+ (Chrome, Edge, or Brave)

## Build & Test Commands

### Quick Reference
```bash
# Quick build (backend only)
dotnet build

# Full build (frontend + backend)
npm install && npm run build && dotnet build

# Run tests
dotnet test

# Lint TypeScript
npm run lint

# Watch mode for development
npm run watch
```

### Standard Build Commands

**Canonical Build Commands (all environments):**
- **Full build (recommended)**: `dotnet build /p:BuildingProject=true`
- **Release build**: `dotnet build --configuration Release /p:BuildingProject=true`
- **Debug build**: `dotnet build --configuration Debug /p:BuildingProject=true`
- **Skip npm build**: `dotnet build /p:SkipJavaScriptBuild=true`
- **Clean and build**: `dotnet clean && dotnet build /p:BuildingProject=true`

**Build Shortcuts (easier to type):**
- **Full build**: `dotnet build -p:FullBuild=true`
- **Quick build (skip npm)**: `dotnet build -p:Quick=true`
- **Production build**: `dotnet build -p:Production=true` (Release + Full)
- **Standard build**: `dotnet build` (C# only, no TypeScript)

**Legacy/Alternative Commands:**
- **Build solution**: `dotnet build Extension.sln`
- **Build C# only**: `dotnet build`
- **Manual frontend build**: `cd Extension && npm install && npm run build`

### Frontend Build Commands (TypeScript/JavaScript)
Must be run from the `Extension/` directory:
- **Install dependencies**: `npm install`
- **Full frontend build**: `npm run build` (runs both ES6 and esbuild)
- **ES6 TypeScript only**: `npm run build:es6`
- **Bundle with esbuild only**: `npm run bundle:esbuild`
- **Development build**: `npm run build:dev`
- **Production build**: `npm run build:prod`
- **Watch mode (concurrent)**: `npm run watch`
- **Watch TypeScript only**: `npm run watch:tsc`
- **Watch esbuild only**: `npm run watch:esbuild`
- **Type checking (no emit)**: `npm run typecheck`
- **Clean build artifacts**: `npm run clean`

### Linting & Code Quality Commands

#### TypeScript Linting
From `Extension/` directory:
- **Run ESLint**: `npm run lint`
- **Fix ESLint issues**: `npm run lint:fix`
- **Type check only**: `npm run typecheck`

#### C# Code Quality
- **Format code**: `dotnet format`
- **Analyze code**: `dotnet build /p:RunAnalyzers=true`
- **Check format**: `dotnet format --verify-no-changes`
- **Style violations**: `dotnet build /p:EnforceCodeStyleInBuild=true`

### Testing Commands

#### xUnit Tests (.NET)
- **Run all tests**: `dotnet test`
- **Run with detailed output**: `dotnet test --logger "console;verbosity=detailed"`
- **Run single test**: `dotnet test --filter "FullyQualifiedName=Extension.Tests.<TestClassName>.<TestMethodName>"`
- **Run test class**: `dotnet test --filter "ClassName=<TestClassName>"`
- **Run with coverage**: `dotnet test --collect:"XPlat Code Coverage"`
- **Coverage with report**: `dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults`
- **Watch mode**: `dotnet watch test`
- **Run in parallel**: `dotnet test --parallel`
- **No build**: `dotnet test --no-build`

#### Test Examples
```bash
# Run a specific test method
dotnet test --filter "FullyQualifiedName=Extension.Tests.Services.StorageServiceTests.TestMethod"

# Run all tests in a namespace
dotnet test --filter "FullyQualifiedName~Extension.Tests.Services"

# Run tests matching a pattern
dotnet test --filter "DisplayName~Should"
```

### Order of Operations
For full build from clean state:
1. `cd Extension` (if not already in Extension directory)
2. `npm install` (first time or when package.json changes)
3. `npm run build` (builds TypeScript to JavaScript)
4. `cd ..` (back to solution root)
5. `dotnet build` (builds C# and packages extension)

### Development Commands
- **Install extension in browser**: Build, then load unpacked from `Extension/bin/Release/net9.0/browserextension`
- **Watch all**: `cd Extension && npm run watch` (in one terminal) + `dotnet watch build` (in another)
- **View browser extension logs**: Open browser DevTools (F12) → Console → Filter by extension
- **Debug Blazor WASM**: Set `builder.Logging.SetMinimumLevel(LogLevel.Debug)` in Program.cs
- **Clear extension storage**: `chrome.storage.local.clear()` in browser console
- **Reload extension**: chrome://extensions → Click reload button on extension card
- **Check manifest**: Validate at `Extension/bin/Release/net9.0/browserextension/manifest.json`

### CI/CD Commands
```bash
# Full CI build
cd Extension && npm ci && npm run build:prod && cd .. && dotnet build -c Release

# Run all checks
cd Extension && npm run typecheck && npm run lint && cd .. && dotnet format --verify-no-changes && dotnet test

# Package for distribution
dotnet publish -c Release
```

### Troubleshooting Commands
- **Clean everything**: `dotnet clean && cd Extension && npm run clean && rm -rf node_modules dist`
- **Reinstall dependencies**: `cd Extension && rm -rf node_modules package-lock.json && npm install`
- **Check TypeScript config**: `cd Extension && npx tsc --showConfig`
- **List outdated packages**: `cd Extension && npm outdated`
- **Audit dependencies**: `cd Extension && npm audit`
- **Fix npm vulnerabilities**: `cd Extension && npm audit fix`

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
- Use async/await over promise chains
- Export types/interfaces alongside implementations
- File naming: camelCase for .ts files (e.g., contentScript.ts)
- Use ES6+ features (template literals, destructuring, spread operator)

### TypeScript Patterns for Browser Extension

#### Service Worker (background script)
- Location: `wwwroot/scripts/esbuild/service-worker.ts`
- Must be bundled via esbuild: `npm run bundle:esbuild`
- Handles all extension lifecycle events
- Manages chrome.runtime message routing
- No DOM access, runs in background context

#### Content Script
- Location: `wwwroot/scripts/esbuild/IsolatedWorldContentScript.ts`
- Injected conditionally into web pages
- Runs in isolated context, separate from page scripts
- Bridge between web page and extension via polaris-web
- Must handle message validation and sanitization

#### Signify TypeScript Shim
- Location: `wwwroot/scripts/esbuild/signify_ts_shim.ts`
- Provides JavaScript-C# interop layer for signify-ts
- Bundled with dependencies via esbuild
- Paired with `Services/SignifyService/Signify_ts_shim.cs`

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

- For understanding of the terms KERI, ACDC, OOBI, and IPEX, see the following resource: <https://github.com/GLEIF-IT/vlei-trainings/blob/main/markdown/llm_context.md>
- CESR (Compact Event Streaming Representation): Self-describing binary encoding format used throughout KERI/ACDC for cryptographic primitives and data structures. Key point: preserves field ordering for deterministic serialization
- Interaction patterns for those protocols, including key management, ACDC (i.e., a credential or attestation) interactions between the roles of Issuer, Holder, and Verifier:
  - Issuer creates credential
  - Issuer issues credential to Holder
  - Holder receives credential from Issuer
  - Holder presents credential to Verifier
  - Verifier verifies credential from Holder

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

### Testing Recommendations

- Test with actual signify-ts output to ensure compatibility
- Validate edge cases with empty/null nested objects
- Use integration tests to verify end-to-end serialization
- Create unit tests that detect breaking changes in signify-ts types
- Mock JavaScript interop calls in unit tests using test doubles
- Test timeout and cancellation scenarios for async operations

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
**Solution**: Target Manifest V3 for Chrome/Edge, check Firefox compatibility separately

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

The extension enforces strict security boundaries:

- **Passcode Caching**: Maximum 5 minutes of inactivity before automatic clearing
- **Content Script Messages**: Only accepted from active tab during/after authentication
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
- **KERIA Communication**: All agent communications via authenticated signify-ts

## Priority Order for Code Changes

When making changes, prioritize in this order:
1. **Security** - Never expose keys, secrets, or sensitive data
2. **Functionality** - Ensure core KERI/ACDC operations work correctly
3. **Type Safety** - Maintain strong typing across C#/TypeScript boundary
4. **Performance** - Optimize after functionality is verified
5. **Code Style** - Apply formatting rules last

## Testing Strategy

### Test Frameworks and Tools
- **Unit Tests**: xUnit with Moq for mocking dependencies
- **Integration Tests**: Test signify-ts interop with actual output from KERIA
- **Browser Testing**: Manual testing required for extension UI/UX flows
- **Test Data**: Use local schema files in `Extension/Schemas/` for credential validation

### Testing Commands (Strategy)
- Run all tests: `dotnet test`
- Run with coverage: `dotnet test --collect:"XPlat Code Coverage"`
- Run specific test class: `dotnet test --filter "ClassName=TestClassName"`
- Run specific test method: `dotnet test --filter "FullyQualifiedName=Extension.Tests.ClassName.MethodName"`
- Run tests in watch mode: `dotnet watch test`

### Key Test Areas
- **SignifyService**: Mock JavaScript interop, test timeout/cancellation
- **Message Handlers**: Validate message routing and security boundaries
- **Storage Service**: Test chrome.storage API interactions
- **Credential Schemas**: Validate against local vLEI schemas
- **CESR/SAID Ordering**: Test preservation of field order in serialization

## Browser Extension Configuration

### Manifest Configuration (manifest.json)
- **Manifest Version**: V3 (required for Chrome Web Store)
- **Compatibility**: Chrome, Edge, Brave (Chromium-based browsers 127+)
- **Permissions**:
  - Minimum required permissions declared
  - Host permissions requested at runtime when needed
- **Content Security Policy**:
  - Strict CSP preventing inline scripts
  - No eval() or dynamic code execution
  - Trusted sources explicitly listed

### Extension Components
- **Background Worker**: `scripts/esbuild/BackgroundWorker.js` (persistent background script)
- **Content Script**: Injected on-demand, not automatically
- **Action Popup**: Blazor WASM UI in popup window
- **Options Page**: Full tab for extended configuration

### Build Output Structure

```text
Extension/bin/Release/net9.0/browserextension/
├── manifest.json
├── _framework/          # Blazor WASM files
├── scripts/
│   ├── esbuild/        # Bundled JS for extension
│   └── es6/            # Compiled TypeScript modules
└── icons/              # Extension icons
```

## Dependency Management

### C# Dependencies (NuGet)
Managed in `.csproj` files:
- **MudBlazor**: UI component library
- **Microsoft.AspNetCore.Components.WebAssembly**: Blazor WASM framework
- **Newtonsoft.Json**: JSON serialization (consider System.Text.Json for new code)
- **Polly**: Resilience and transient fault handling
- **xUnit/Moq**: Testing frameworks

### JavaScript Dependencies (npm)
Managed in `Extension/package.json`:
- **signify-ts**: Pinned to commit `78d0a694...` for KERI operations
- **signify-polaris-web**: Pinned to commit `7d7dd13` for web page protocol
- **libsodium-wrappers-sumo**: Cryptographic operations
- **esbuild**: JavaScript bundler for extension scripts
- **TypeScript**: Version 5.4.2 for type safety

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

### File Lock Detection

The build system includes automatic detection and user guidance:

```xml
<Target Name="CheckFileLocks" BeforeTargets="InstallDependencies">
  <!-- Detects WSL vs Windows environment and provides guidance -->
</Target>
```

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
cd /mnt/c/s/k/k-b-w
rm -rf Extension/bin Extension/obj Extension.Tests/bin Extension.Tests/obj
rm -rf Extension/node_modules/.cache Extension/dist
dotnet clean
dotnet restore --force-evaluate
dotnet build /p:BuildingProject=true
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
| `cd Extension && npm run build` | Any | TypeScript only | Manual script build |
| `dotnet clean` | Any | Clean .NET outputs | Safe for both environments |

## Debugging Tips

- Browser extension logs: Check browser console (F12) and extension service worker logs
- Blazor WASM debugging: Enable detailed errors in `Program.cs` for development
- signify-ts issues: Use `console.log`/`console.debug` in TypeScript, visible in browser console
- Network issues: Check CORS policies and content security policy (CSP) settings
- TypeScript compilation issues: Check `tsconfig.json` for module resolution settings
- ESLint issues: Run `npx eslint --fix` to auto-fix style issues

### Build Troubleshooting

#### NuGet Package Resolution Issues
If you encounter "Package not found" errors or path-related build issues:
1. **Force restore packages**: `cd Extension && dotnet restore --force && cd ..`
2. **Clean and rebuild**: `rm -rf Extension/obj Extension.Tests/obj && dotnet build -p:Quick=true`
3. **If Visual Studio was open**: Close it completely, then clean and rebuild from WSL

#### Common Build Error Solutions
- **"TargetPath not specified" errors**: Usually indicates WSL/Windows path conflicts. Clean obj directories and rebuild
- **"Package Blazor.BrowserExtension.Build not found"**: Run `dotnet restore --force` from the Extension directory
- **Build succeeds after restore**: This is normal - the first restore may need to download packages