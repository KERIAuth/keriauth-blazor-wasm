# Coding Standards

For architecture overview, see [ARCHITECTURE.md](ARCHITECTURE.md). For build instructions, see [BUILD.md](BUILD.md). For operational directives, see [CLAUDE.md](CLAUDE.md).

## General Principles

- **Minimize TypeScript** — stay in C# where possible. Only use TypeScript where browser APIs or signify-ts require it.
- **Never create new .js files** — always use TypeScript (.ts) that compiles to JavaScript.
- **Prefer Records over Classes** for immutable data types, DTOs, value objects, and model classes.
- **Use FluentResults** `Result<T>` for error handling, not exceptions for control flow.
- **Avoid over-abstraction** — not everything needs a separate helper, service, or interface.
- **Don't remove existing code comments** — they may contain TODO items or design rationale.

## C# Guidelines

### Style
- .NET 9.0 SDK
- 4-space indentation
- Opening braces on same line
- Braces for all control structures, even single-line
- Expression-bodied members for simple methods
- EnforceCodeStyleInBuild enabled

### Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Classes, interfaces | PascalCase (I prefix for interfaces) | `IStorageService` |
| Public methods, properties | PascalCase | `GetCredentialAsync` |
| Private fields | _camelCase | `_jsRuntime` |
| Constants | const/readonly | `const int MaxRetries = 3` |

### Types and Nullability
- Nullable reference types enabled — default to non-nullable, use `?` for nullable
- Use explicit types; `var` only when type is obvious
- Prefer Records for: JSON models, API types, configuration, state representations
- Use Classes only for service implementations with mutable behavior
- Comment and justify where there is use of `object`

### Async
- async/await consistently; avoid blocking calls and sync-over-async
- Suffix async methods with `Async`, return `Task` or `Task<T>`
- Use CancellationToken for all async operations to external services, including signifyClient to signify-ts (to KERIA)
- Default timeout: 30 seconds for network operations

### Error Handling
- Use `Result<T>` from FluentResults for all methods that can fail
- `Result.Ok()` for success, `Result.Fail("message")` for failure
- Try/catch with specific exception types (e.g. dotnet vs jsRuntime), not catch-all
- Never use exceptions for control flow, unless necessary and commented

### Imports
- System namespaces first, then project namespaces
- Sorted alphabetically within groups
- At top of file, outside namespace declarations

### Other
- Use UTC for all date/time values
- American English spelling (e.g., "canceled" not "cancelled")
- XML docs for public APIs; inline comments for implementation details; avoid regions
- Constructors for setting properties; avoid public setters

## TypeScript Guidelines

### Style
- 2-space indentation
- `const` for immutable values, `let` for mutable, never `var`
- Prefer arrow functions for callbacks
- camelCase for .ts file names
- ES6+ features (template literals, destructuring, spread)

### Types
- Strong types — define interfaces/types for all data structures
- Avoid `any` — use `unknown` if type is truly dynamic
- Export types/interfaces alongside implementations

### Async
- async/await over promise chains
- Exception: some intentional promise chains exist to avoid known race conditions or maintain user context (e.g., Action OnClick handling)

### JsBind.Net for Interop

Preferred approach order:
1. **Webextensions.Net** — use when available
1. **JsBind.Net** — robust JS interop without requiring new script files
2. **IJSRuntime with existing browser APIs** — for basic JavaScript calls
3. **TypeScript modules** — only when JsBind.Net and IJSRuntime can't handle it

### TypeScript Patterns

| Component | Location | Purpose |
|-----------|----------|---------|
| Content Script | `scripts/bundles/src/ContentScript.ts` | Web page / extension bridge |
| Signify Client | `scripts/bundles/src/signifyClient.ts` | signify-ts JS-C# interop layer |
| Shared Types | `scripts/types/src/` | Interfaces shared between TS and C# |
| ES6 Modules | `scripts/modules/src/` | Simple modules compiled by tsc |

## KERI / ACDC / CESR

### Resources
- [vLEI Training Context](https://github.com/GLEIF-IT/vlei-trainings/blob/main/markdown/llm_context.md)
- [vLEI Schemas](https://github.com/GLEIF-IT/vLEI-schema) (local copies in `Extension/Schemas/`)

### Credential Interaction Roles
- **Issuer** creates and issues credentials to Holder
- **Holder** receives credentials from Issuer, presents to Verifier
- **Verifier** verifies cryptographically (holder's KEL + presentation signature) and validates (issuer's keys, provenance, revocation status)

### CRITICAL: CESR/SAID Ordering

SAID (Self-Addressing IDentifier) is a cryptographic hash digest that depends on exact JSON field ordering. Standard C# JSON serializers do not preserve insertion order.

**Correct**:
```csharp
// Use RecursiveDictionary — preserves insertion order
RecursiveDictionary credential = await signifyClient.GetCredential(...);

// Convert only at message boundary to JavaScript
var credentialObj = credential.ToJsObject();
```

**NEVER do this**:
```csharp
// Breaks CESR/SAID ordering, invalidates signatures
var json = JsonSerializer.Serialize(credential);
var json = JsonConvert.SerializeObject(credential);
```

Data type rules for credentials:
- From signify-ts: `RecursiveDictionary`
- In C# models: `RecursiveDictionary`
- To JavaScript: `ToJsObject()` at message boundary only
- Never: `string`, `Dictionary<string, object>`, or any JSON round-trip

## C#-TypeScript Interop

### Type Mapping
- signify-ts returns complex nested JSON objects, not primitives
- Expect multi-level hierarchies requiring ordered deserialization
- Some objects have dynamic/optional properties — use nullable types

### Signify-ts Shim
- Location: `Extension/Services/SignifyService/Signify-ts-shim.cs`
- Wraps all signify-ts interop with timeouts, cancellation, and error handling
- 30-second default timeout for network operations
- All JS interop calls in try-catch with specific error messages

### Security-Compliant Interop

**Use TypeScript modules with import:**
```csharp
var module = await _jsRuntime.InvokeAsync<IJSObjectReference>(
    "import", "./scripts/es6/MyModule.js");
await module.InvokeVoidAsync("MyFunction", params);
```

**NEVER use eval or dynamic code execution:**
```csharp
// ALL OF THESE ARE FORBIDDEN
await _jsRuntime.InvokeVoidAsync("eval", ...);
await _jsRuntime.InvokeAsync<object>("Function", ...);
```

## Common Libraries

| Library | Purpose |
|---------|---------|
| FluentResults | Error handling with `Result<T>` |
| MudBlazor | UI components |
| xUnit + Moq | Testing |
| System.Text.Json | JSON serialization (NOT for credentials) |
| signify-ts | KERI/ACDC operations |
| polaris-web | Web page communication protocol |
| JsBind.Net | JavaScript interop |

## Dependency Injection

- Built-in DI container
- Register services in `Program.cs` using `AddScoped`, `AddTransient`, or `AddSingleton` (usually)
- Primarily singleton lifetime for state management across the extension
- Use `IOptions<AppSettings>` for configuration; avoid hard-coded values

## vLEI Credential Schemas

Local copies in `Extension/Schemas/`:
- QVI (Qualified vLEI Issuer)
- LE (Legal Entity)
- OOR (Official Organizational Role)
- OOR AUTH (OOR Authorization)
- ECR (Engagement Context Role)
- ECR AUTH (ECR Authorization)
- iXBRL (Verifiable iXBRL Report Attestation)

Schema repository: https://github.com/GLEIF-IT/vLEI-schema
