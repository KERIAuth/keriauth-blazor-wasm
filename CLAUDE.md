# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build/Run Commands

### Standard Build Commands
- Build backend only: `dotnet build`
- Build with frontend: `npm install && npm run build && dotnet build`
- Clean build: `dotnet clean && npm run build && dotnet build`
- Run development server: `dotnet run` (specify if available)

### Frontend Build Commands
- Install dependencies: `npm install`
- Frontend build (all): `npm run build`
- ES6 TypeScript build only: `npm run build:es6`
- Bundle with esbuild only: `npm run bundle:esbuild`

### Testing Commands
- Run all tests: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=<TestClassName>.<TestMethodName>"`
- Code quality checks: `dotnet format --verify-no-changes` (if configured)

### Order of Operations
For full build from clean state:
1. `npm install` (first time or when package.json changes)
2. `npm run build` (builds TypeScript to JavaScript)
3. `dotnet build` (builds C# and packages extension)

## Development Environment

Primary development environment is Windows running WSL2. Commands above work in both Windows PowerShell/Command Prompt and WSL2 bash. Claude Code operates within the WSL2 environment.

## Project Structure

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

## C# Code Style Guidelines

- .NET 9.0 SDK required with rollForward: latestMinor (see global.json)
- 4-space indentation
- Opening braces on same line
- PascalCase for classes, interfaces (with 'I' prefix), public methods and properties
- Private fields: camelCase with underscore prefix (_fieldName)
- Constants: use readonly fields or const values (prefer const for immutable values)
- Nullable reference types enabled - use explicit null checks
- Use expression-bodied members for simple methods
- Try/catch with specific exception types
- Organize imports at top of file, outside namespace declarations
- Test pattern: Arrange-Act-Assert with descriptive test names using xUnit
- EnforceCodeStyleInBuild enabled in project files
- Async methods suffixed with "Async" returning Task/Task&lt;T&gt;
- XML comments on public APIs
- Use FluentResults for operation results (Result&lt;T&gt; pattern)
- Use Records versus Struct or Class definitions where the structure is used in many contexts, to enable immutable testing and future functional versus object-oriented approaches
- Use generic functions when there are multiple types that will be applicable, including for message payload request and response types

## TypeScript Code Style Guidelines

- Use strong types - define interfaces/types for all data structures
- Avoid use of "any" type where possible - use "unknown" if type is truly dynamic
- 2-space indentation (common TypeScript convention)
- Use `const` for immutable values, `let` for mutable ones, avoid `var`
- Prefer arrow functions for callbacks and short functions
- Use async/await over promise chains
- Export types/interfaces alongside implementations
- File naming: camelCase for .ts files (e.g., contentScript.ts)
- Use ES6+ features (template literals, destructuring, spread operator)

## Architecture Notes
The extension follows a multi-component architecture:
1. **Service Worker** - Background script handling extension lifecycle and message routing
2. **Content Script** - Injected into web pages, bridges page and extension communication
3. **Blazor WASM App** - UI layer for extension popup and tabs
4. **SignifyService** - Manages KERI/ACDC operations via signify-ts JavaScript interop

Services use dependency injection with primarily singleton lifetime for state management across the extension. The StorageService provides persistent storage using chrome.storage API.

## Use of KERI, ACDC, and CESR via signify-ts
- For understanding of the terms KERI, ACDC, OOBI, and IPEX, see the following resource: https://github.com/GLEIF-IT/vlei-trainings/blob/main/markdown/llm_context.md
- CESR (Compact Event Streaming Representation): Self-describing binary encoding format used throughout KERI/ACDC for cryptographic primitives and data structures. Key point: preserves field ordering for deterministic serialization
- Interaction patterns for those protocols, including key management, ACDC (i.e., a credential or attestation) interactions between the roles of Issuer, Holder, and Verifier:
  - Issuer creates credential
  - Issuer issues credential to Holder
  - Holder receives credential from Issuer
  - Holder presents credential to Verifier
  - Verifier receives credential from Issuer

### vLEI Credential Schema Definitions
Schema repository: https://github.com/GLEIF-IT/vLEI-schema

Credential types and their schemas:
- **QVI** (Qualified vLEI Issuer) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/qualified-vLEI-issuer-vLEI-credential.json
  - Purpose: Issued by GLEIF to authorized vLEI issuers
  
- **LE** (Legal Entity) vLEI Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-vLEI-credential.json
  - Purpose: Issued by QVI to legal entities
  
- **OOR** (Official Organizational Role) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-official-organizational-role-vLEI-credential.json
  - Purpose: Issued to individuals in official roles within legal entities
  
- **OOR AUTH** (OOR Authorization) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/oor-authorization-vlei-credential.json
  - Purpose: Authorization from LE to QVI to issue OOR credentials
  
- **ECR** (Engagement Context Role) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/legal-entity-engagement-context-role-vLEI-credential.json
  - Purpose: Issued for specific engagement contexts (e.g., supplier relationships)
  
- **ECR AUTH** (ECR Authorization) Credential
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/ecr-authorization-vlei-credential.json
  - Purpose: Authorization from LE to QVI to issue ECR credentials
  
- **iXBRL** (Verifiable iXBRL Report Attestation)
  - Schema: https://raw.githubusercontent.com/GLEIF-IT/vLEI-schema/main/verifiable-ixbrl-report-attestation.json
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
   <!--  - create unit tests that detect issues with C# models used for interop, e.g. in the case where signify-ts provided types have changed
   -->

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
    public string Status { get; set; }
    public PayloadData Payload { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}

public class PayloadData
{
    public string Id { get; set; }
    public List<NestedItem> Items { get; set; }
    // Use JObject for variable structures
    public JObject AdditionalData { get; set; }
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

## Priority Order for Code Changes

When making changes, prioritize in this order:
1. **Security** - Never expose keys, secrets, or sensitive data
2. **Functionality** - Ensure core KERI/ACDC operations work correctly
3. **Type Safety** - Maintain strong typing across C#/TypeScript boundary
4. **Performance** - Optimize after functionality is verified
5. **Code Style** - Apply formatting rules last

## Debugging Tips

- Browser extension logs: Check browser console (F12) and extension service worker logs
- Blazor WASM debugging: Enable detailed errors in `Program.cs` for development
- signify-ts issues: Use `console.log` in TypeScript, visible in browser console
- Network issues: Check CORS policies and content security policy (CSP) settings