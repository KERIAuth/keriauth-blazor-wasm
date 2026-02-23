# CLAUDE.md

Operational directives for Claude Code working in this repository. For architectural context, see [ARCHITECTURE.md](ARCHITECTURE.md). For build instructions, see [BUILD.md](BUILD.md). For coding standards, see [CODING.md](CODING.md).

## Invariants

These are firm constraints. Do not change or work around them without explicit approval.

### Architecture Invariants

1. **Two Blazor WASM runtimes**: One BackgroundWorker (singleton service worker), many App instances (tabs, popup, SidePanel). These are separate .NET runtimes with separate DI containers and IJSRuntime instances.
2. **Port-based messaging** with HELLO/READY/ATTACH_TAB handshake between Content Script, App, and BackgroundWorker.
3. **signify-ts via JS interop** for all KERI/ACDC operations. Stay in C# and TypeScript. Minimize TypeScript surface area.
4. **Content script is a thin bridge** — no WASM, no sensitive data. Bridges web page and extension via polaris-web.
5. **MudBlazor** for all UI components.
6. **RecursiveDictionary** for CESR/SAID-safe credential handling. Preserves insertion order required for cryptographic verification. Open to JSON serializer alternatives that guarantee strict ordering, but not a priority to change.
7. **Service worker can become inactive at any time** — Chrome controls its lifecycle (standby, browser restart, idle timeout). App instances must be resilient to this and not force a service worker restart until actually needed.
8. **App is very state-aware** — many states, primarily captured in AppCache.cs. This in-memory caching approach is working and performant. Do not change it in the short term.
9. **Not everything needs separate helpers/services with interfaces** — avoid over-abstraction.
10. **Prefer reactive/observable patterns over imperative state management** — use `IObservable<T>`, computed properties, and storage change notifications to propagate state. Avoid imperative "fetch and set" patterns where a reactive subscription or computed property would keep state consistent automatically.

### Security Invariants

11. **Passcode and private keys never reach content script or web page.**
12. **No eval, no dynamic scripts, strict CSP.** Never use `eval()`, `Function()`, `chrome.tabs.executeScript()`, or any runtime code generation. All JavaScript must be in static files.
13. **NEVER serialize/deserialize credentials using System.Text.Json or Newtonsoft.Json** — this breaks CESR/SAID field ordering and invalidates cryptographic signatures. Use RecursiveDictionary.

### Adjustable (not invariants)

- Session timeout duration is user-configurable via preferences
- Whether content script messages extend the session — open to change

## Working Style with Claude

### Phased Plans — MANDATORY

**All non-trivial work must follow phased plans that the user approves first.**

1. **Propose a plan before writing code** — use plan mode for anything beyond trivial fixes
2. **Wait for explicit approval before starting each phase** — never auto-proceed to the next phase
3. **After completing a phase**: summarize what was done, report test results, ask "Ready for Phase N?"
4. **If unit tests pass but manual browser testing is needed**: stop and wait for user confirmation
5. **When a session runs out of context and restarts**: re-read this file. The phased constraint still applies. Check where you left off before continuing.

### Change Discipline

- **Small, incremental changes** — one phase at a time
- **Don't assume standard patterns apply** across the C#/TypeScript/browser extension/webpage boundary — ask first when uncertain
- **Never edit CLAUDE.md in bulk** — propose diffs for user review
- **Never remove code comments** — they may contain TODO items or design rationale that is important. Otherwise, code should not be heavily commented.
- **Don't add comments, docstrings, or type annotations** to code you didn't change
- **Avoid over-engineering** — three similar lines is better than a premature abstraction
- **Avoid backwards-compatibility hacks** — if something is unused, delete it completely

### Priority Order for Code Changes

1. **Security** — never expose keys, secrets, or sensitive data
2. **Functionality** — ensure core KERI/ACDC operations work correctly
3. **Type Safety** — maintain strong typing across C#/TypeScript boundary
4. **Performance** — optimize after functionality is verified
5. **Code Style** — apply formatting rules last

## Building This Project

See [BUILD.md](BUILD.md) for full instructions. Builds exclusively in WSL (Ubuntu).

**Quick reference for Claude Code**:
- Incremental build: `make build`
- Build + test: `make build && make test`
- TypeScript only: `make build-ts`
- C# only (skip TypeScript): `dotnet build --configuration Release -p:Quick=true`
- Clean build: `make clean-build`
- Watch mode: `make watch`

**Build configuration**: Release only. Debug configuration is intentionally disabled.

## Security Constraints

- **Passcode caching**: cleared after configurable inactivity timeout
- **Content script messages**: only accepted from active tab during/after authentication or after a signing association exists
- **HTTP header signing**: safe methods (GET) can be auto-approved with explicity user preference for a given site; unsafe methods (POST, PUT, DELETE) require explicit user consent
- **Script execution**: strict CSP, no dynamic/inline scripts, no eval, no runtime code generation
- **Data isolation**: sensitive data (passcode, private keys) never reaches content script or web page
- **Storage**: chrome.storage.local for non-sensitive data only
- **Permissions**: minimum required and optional permissions declared in manifest
- **KERIA communication**: all KERIA agent communications via authenticated signify-ts

## Testing Strategy

### Test Approach

**Unit Tests** (xUnit with Moq): Fast feedback, Arrange-Act-Assert pattern, mock IJSRuntime for browser-free testing.

**Integration Tests**: Verify signify-ts interop with actual KERIA output. Test with real signify-ts responses to catch type mismatches early.

**Manual Browser Testing**: Required after significant changes to BackgroundWorker, ContentScript, or UI flows. Extension UI/UX, service worker behavior, and chrome.* APIs cannot be fully automated.

### Test Priority

1. **CESR/SAID ordering** — verify RecursiveDictionary preserves insertion order through C#/JS boundary
2. **JavaScript interop** — mock IJSRuntime calls, test timeout/cancellation, null/undefined edge cases
3. **Port message routing & security boundaries** — validate 3-boundary system, ensure passcode never leaves BackgroundWorker
4. **Storage service** — serialization, storage limits, concurrent access
5. **Credential schema validation** — catch schema drift from vLEI spec updates

### Testing Anti-Patterns

- Never serialize credentials with System.Text.Json or Newtonsoft.Json
- Don't mock BackgroundWorker service worker events — test actual chrome.* APIs or use integration tests
- Don't test browser caching behavior — document it instead

## Reference Documents

- [ARCHITECTURE.md](ARCHITECTURE.md) — system structure, components, flows
- [BUILD.md](BUILD.md) — build commands, troubleshooting, environment setup
- [CODING.md](CODING.md) — C#, TypeScript, and interop coding standards
- [PAGE-CS-MESSAGES.md](PAGE-CS-MESSAGES.md) — web page / content script message protocol
- [POLARIS_WEB_COMPLIANCE.md](POLARIS_WEB_COMPLIANCE.md) — supported polaris-web capabilities
