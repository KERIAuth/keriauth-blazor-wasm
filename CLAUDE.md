# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build/Run Commands

- Build: `dotnet build`
- Build with frontend: `npm install && npm run build && dotnet build`
- Test: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=Extension.Tests.Helper.UrlBuilderTests.CreateUrlWithEncodedQueryStrings_EncodesQueryParamsCorrectly"`
- Frontend build: `npm run build`
- ES6 TypeScript build: `npm run build:es6`
- Bundle with esbuild: `npm run bundle:esbuild`
- Clean build: `dotnet clean && npm run build && dotnet build`

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

## Typescript Code Style Guidelines
- Use strong types
- Avoid use of "any" time where possible

## Architecture Notes
The extension follows a multi-component architecture:
1. **Service Worker** - Background script handling extension lifecycle and message routing
2. **Content Script** - Injected into web pages, bridges page and extension communication
3. **Blazor WASM App** - UI layer for extension popup and tabs
4. **SignifyService** - Manages KERI/ACDC operations via signify-ts JavaScript interop

Services use dependency injection with primarily singleton lifetime for state management across the extension. The StorageService provides persistent storage using chrome.storage API.

## Use of KERI, ACDC, and CESR via signify-ts
- For understanding of the terms KERI, ACDC, OOBI, IPEX, and CESR and how they work, see the following resource: https://github.com/GLEIF-IT/vlei-trainings/blob/main/markdown/llm_context.md
- Interaction patterns for those protocols, including key management, ACDC (i.e., a credential or attestation) interactions between the roles of Issuer, Holder, and Verifier:
  - Issuer creates credential
  - Issuer issues credential to Holder
  - Holder receives credential from Issuer
  - Holder presents credential to Verifier
  - Verifier receives credential from Issuer 

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
- Shim should handle common interaction patterns and potential issues
- For the signify-ts implementations that create network requests or wait for responses, these should implement timeouts and cancellation

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