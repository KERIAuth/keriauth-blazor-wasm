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

### Standard Build Commands
- Build backend only: `dotnet build`
- Build with frontend: `npm install && npm run build && dotnet build`
- Clean build: `dotnet clean && npm run build && dotnet build`

### Frontend Build Commands
- Install dependencies: `npm install`
- Frontend build (all): `npm run build`
- ES6 TypeScript build only: `npm run build:es6`
- Bundle with esbuild only: `npm run bundle:esbuild`

### Testing Commands
- Run all tests: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=Extension.Tests.<TestClassName>.<TestMethodName>"`
- Run test with coverage: `dotnet test --collect:"XPlat Code Coverage"`
- Run specific test class: `dotnet test --filter "ClassName=<TestClassName>"`

### Order of Operations
For full build from clean state:
1. `npm install` (first time or when package.json changes)
2. `npm run build` (builds TypeScript to JavaScript)
3. `dotnet build` (builds C# and packages extension)

### Development Commands
- Install extension in browser: Build, then load unpacked from `Extension/bin/Release/net9.0/browserextension`
- Debug mode build: `dotnet build -c Debug`
- Watch TypeScript changes: `npm run build:es6 -- --watch`
- Watch esbuild changes: `npm run bundle:esbuild -- --watch`
- View browser extension logs: Open browser DevTools (F12) → Console → Filter by extension
- Debug Blazor WASM: Set `builder.Logging.SetMinimumLevel(LogLevel.Debug)` in Program.cs
- Clear extension storage: chrome.storage.local.clear() in browser console
- Run ESLint: `npx eslint wwwroot/scripts/**/*.ts`

## TypeScript Coding Guidelines

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
- Location: `wwwroot/scripts/esbuild/ContentScript.ts`
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
- **Data Structures**: Use Records versus Struct or Class definitions where the structure is used in many contexts, to enable immutable testing and future functional versus object-oriented approaches
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

- For understanding of the terms KERI, ACDC, OOBI, and IPEX, see the following resource: https://github.com/GLEIF-IT/vlei-trainings/blob/main/markdown/llm_context.md
- CESR (Compact Event Streaming Representation): Self-describing binary encoding format used throughout KERI/ACDC for cryptographic primitives and data structures. Key point: preserves field ordering for deterministic serialization
- Interaction patterns for those protocols, including key management, ACDC (i.e., a credential or attestation) interactions between the roles of Issuer, Holder, and Verifier:
  - Issuer creates credential
  - Issuer issues credential to Holder
  - Holder receives credential from Issuer
  - Holder presents credential to Verifier
  - Verifier verifies credential from Holder

### vLEI Credential Schema Definitions

Schema repository: https://github.com/GLEIF-IT/vLEI-schema
Local copies: `Extension/Schemas/`

Credential types and their schemas:
- **QVI** (Qualified vLEI Issuer) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/qualified-vLEI-issuer-vLEI-credential.json
  - Local: `Extension/Schemas/qualified-vLEI-issuer-vLEI-credential.json`
  - Purpose: Issued by GLEIF to authorized vLEI issuers
  
- **LE** (Legal Entity) vLEI Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-vLEI-credential.json
  - Local: `Extension/Schemas/legal-entity-vLEI-credential.json`
  - Purpose: Issued by QVI to legal entities
  
- **OOR** (Official Organizational Role) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-official-organizational-role-vLEI-credential.json
  - Local: `Extension/Schemas/legal-entity-official-organizational-role-vLEI-credential.json`
  - Purpose: Issued to individuals in official roles within legal entities
  
- **OOR AUTH** (OOR Authorization) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/oor-authorization-vlei-credential.json
  - Local: `Extension/Schemas/oor-authorization-vlei-credential.json`
  - Purpose: Authorization from LE to QVI to issue OOR credentials
  
- **ECR** (Engagement Context Role) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-engagement-context-role-vLEI-credential.json
  - Local: `Extension/Schemas/legal-entity-engagement-context-role-vLEI-credential.json`
  - Purpose: Issued for specific engagement contexts (e.g., supplier relationships)
  
- **ECR AUTH** (ECR Authorization) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/ecr-authorization-vlei-credential.json
  - Local: `Extension/Schemas/ecr-authorization-vlei-credential.json`
  - Purpose: Authorization from LE to QVI to issue ECR credentials
  
- **iXBRL** (Verifiable iXBRL Report Attestation)
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/verifiable-ixbrl-report-attestation.json
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

### Issue: JSON property order matters for CESR/SAID
**Solution**: Use `Dictionary<string, object>` with custom serializer that preserves insertion order, or use `LinkedHashMap` equivalent

### Issue: signify-ts returns undefined/null unexpectedly
**Solution**: Always use nullable types in C# DTOs and implement null-conditional operators (`?.`) in service layer

### Issue: Browser extension manifest version conflicts
**Solution**: Target Manifest V3 for Chrome/Edge, check Firefox compatibility separately

## Security Constraints

The extension enforces strict security boundaries:

- **Passcode Caching**: Maximum 5 minutes of inactivity before automatic clearing
- **Content Script Messages**: Only accepted from active tab during/after authentication
- **HTTP Header Signing**: 
  - Safe methods (GET) auto-approved
  - Unsafe methods (POST, PUT, DELETE) require explicit user consent
- **Script Execution**: No dynamic or inline scripts allowed (strict CSP)
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

### Testing Commands
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
- **Service Worker**: `scripts/esbuild/service-worker.js` (persistent background script)
- **Content Script**: Injected on-demand, not automatically
- **Action Popup**: Blazor WASM UI in popup window
- **Options Page**: Full tab for extended configuration

### Build Output Structure
```
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

## Debugging Tips

- Browser extension logs: Check browser console (F12) and extension service worker logs
- Blazor WASM debugging: Enable detailed errors in `Program.cs` for development
- signify-ts issues: Use `console.log`/`console.debug` in TypeScript, visible in browser console
- Network issues: Check CORS policies and content security policy (CSP) settings
- TypeScript compilation issues: Check `tsconfig.json` for module resolution settings
- ESLint issues: Run `npx eslint --fix` to auto-fix style issues