# WebAuthn PRF Migration Plan: TypeScript to C#

## Executive Summary

Migrate the WebAuthn PRF implementation from `webauthnCredentialWithPRF.ts` to a pure C# implementation, minimizing JavaScript interop to only the unavoidable `navigator.credentials` API calls.

## Current Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  WebauthnService.cs                                         │
│  (C# wrapper - calls JS for everything)                     │
├─────────────────────────────────────────────────────────────┤
│  webauthnCredentialWithPRF.ts (~695 lines)                  │
│  - Credential creation/authentication                       │
│  - PRF extension handling                                   │
│  - HKDF key derivation                                      │
│  - AES-GCM encrypt/decrypt                                  │
│  - Profile identifier management                            │
│  - Base64/ArrayBuffer conversions                           │
└─────────────────────────────────────────────────────────────┘
```

## Target Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  WebauthnService.cs (enhanced)                              │
│  - Business logic in C#                                     │
│  - Profile identifier via IStorageService                   │
│  - Credential options construction                          │
│  - PRF extension configuration                              │
│  - Result validation and error handling                     │
├─────────────────────────────────────────────────────────────┤
│  CryptoService.cs (new)                                     │
│  - SHA-256 hashing (System.Security.Cryptography)           │
│  - HKDF key derivation (via SubtleCrypto or NuGet)          │
│  - AES-GCM encrypt/decrypt (via SubtleCrypto or NuGet)      │
├─────────────────────────────────────────────────────────────┤
│  INavigatorCredentialsBinding.cs (new - minimal JS interop) │
│  - CreateAsync(options) → CredentialResult                  │
│  - GetAsync(options) → AssertionResult                      │
│  Only ~50 lines of TypeScript shim                          │
└─────────────────────────────────────────────────────────────┘
```

## Design Decisions

### 1. Cryptography Strategy

**Decision: Native .NET crypto + Blazor.SubtleCrypto for AES-GCM only**

| Operation | Current (TypeScript) | Target (C#) |
|-----------|---------------------|-------------|
| SHA-256 hash | `crypto.subtle.digest` | `System.Security.Cryptography.SHA256` (native WASM) |
| Key derivation | HKDF via `crypto.subtle.deriveKey` | `SHA256(profileId + PRF output + "KERI Auth")` (native WASM) |
| AES-GCM encrypt/decrypt | `crypto.subtle.encrypt/decrypt` | Blazor.SubtleCrypto |
| Random bytes | `crypto.getRandomValues` | `System.Security.Cryptography.RandomNumberGenerator` (native WASM) |

**Key Derivation Change**: Replacing HKDF with SHA-256 using profileIdentifier as salt:

```text
Old: PRF output → HKDF(salt=[], info="KERI Auth", hash=SHA-256) → AES-256 key
New: SHA-256(profileIdentifier || PRF output || "KERI Auth") → AES-256 key
```

The `profileIdentifier` is a randomly-generated UUID stored in `StorageArea.Sync`, scoped to the browser profile. This prevents the same authenticator from being used successfully on another browser profile (same machine or different).

**Rationale**:
- SHA-256 is natively supported in Blazor WASM, eliminating JS interop for key derivation
- PRF output is already high-entropy, so HKDF's "extract" phase provides no benefit
- Domain separation ("KERI Auth") is preserved via concatenation
- Profile binding (profileIdentifier salt) prevents cross-profile credential reuse
- Only AES-GCM requires Blazor.SubtleCrypto (not supported natively in browser WASM)

### 2. Navigator Credentials Interop

**Minimal TypeScript shim** (~50 lines) that:
1. Calls `navigator.credentials.create(options)`
2. Calls `navigator.credentials.get(options)`
3. Extracts and serializes results (handling ArrayBuffer → Base64 conversion)
4. Returns PRF extension results

**User Gesture / Transient Activation Concern:**

WebAuthn requires [transient user activation](https://developer.chrome.com/blog/user-activation) - the browser must know the call originated from a user gesture (click, keypress, etc.). This activation can be lost across async boundaries.

Current architecture: `Button Click → C# Handler → IJSRuntime.InvokeAsync → TypeScript shim → navigator.credentials`

**Risk Assessment:**
- Chrome extensions run in a privileged context and may have different activation rules than web pages
- The existing TypeScript implementation works, suggesting activation is preserved through the current interop chain
- Blazor WASM's `IJSRuntime.InvokeAsync` is synchronous within the WASM runtime (only async from .NET's perspective)

**Mitigation:**
- Test early in Phase 2 to verify activation is preserved
- If activation is lost, fallback option: trigger WebAuthn directly from a button's `onclick` handler in TypeScript, with C# only preparing the options

**Key insight from research**: The [Blazor WASM issue #45236](https://github.com/dotnet/aspnetcore/issues/45236) shows that `PublicKeyCredential` objects don't serialize properly. The workaround is to convert `ArrayBuffer` to `Uint8Array` before returning. Your current TypeScript already does this correctly.

### 3. Profile Identifier Storage

Migrate from raw `chrome.storage.sync` calls to `IStorageService`:

```csharp
public record ProfileIdentifierModel
{
    public required string ProfileId { get; init; }
}

// Usage in WebauthnService
var result = await _storageService.GetItem<ProfileIdentifierModel>(StorageArea.Sync);
if (result.IsSuccess && result.Value != null)
{
    return result.Value.ProfileId;
}
// Create new identifier
var newId = Guid.NewGuid().ToString();
await _storageService.SetItem(new ProfileIdentifierModel { ProfileId = newId }, StorageArea.Sync);
return newId;
```

### 4. Credential Options Construction

Move option building to C# for full type safety:

```csharp
public record WebAuthnRegistrationOptions
{
    public required string ResidentKey { get; init; }  // "required" | "preferred" | "discouraged"
    public string? AuthenticatorAttachment { get; init; }  // "platform" | "cross-platform" | null
    public required string UserVerification { get; init; }  // "required" | "preferred" | "discouraged"
    public string Attestation { get; init; } = "none";
    public List<string> Hints { get; init; } = [];
}

public record WebAuthnAuthenticationOptions
{
    public required List<string> AllowedCredentialIds { get; init; }
    public required string UserVerification { get; init; }
    public List<string>? Transports { get; init; }  // Allows specifying known transports for better UX
}
```

## Implementation Phases

### Phase 1: Infrastructure Setup (Foundation)

**Files to create:**
- `Extension/Services/Crypto/ICryptoService.cs` - Interface for crypto operations
- `Extension/Services/Crypto/SubtleCryptoService.cs` - Implementation using SubtleCrypto
- `Extension/Models/Storage/ProfileIdentifierModel.cs` - Storage model

**NuGet packages to add:**
- `Blazor.SubtleCrypto` (v9.0.0)

**Tasks:**
1. Add NuGet package reference
2. Create `ICryptoService` interface with methods:
   - `byte[] Sha256(byte[] data)` - synchronous, native .NET
   - `byte[] DeriveKeyFromPrf(string profileId, byte[] prfOutput)` - SHA256(profileId || prfOutput || "KERI Auth")
   - `Task<byte[]> AesGcmEncryptAsync(byte[] key, byte[] plaintext, byte[] nonce)`
   - `Task<byte[]> AesGcmDecryptAsync(byte[] key, byte[] ciphertext, byte[] nonce)`
3. Create `ProfileIdentifierModel` record
4. Register services in `Program.cs`

### Phase 2: Minimal Navigator Credentials Binding

**Files to create:**
- `scripts/modules/src/navigatorCredentialsShim.ts` - Minimal JS shim
- `Extension/Services/WebAuthn/NavigatorCredentialsBinding.cs` - C# binding

**TypeScript shim responsibilities:**
```typescript
// navigatorCredentialsShim.ts (~50 lines)
export interface CredentialCreationResult {
    credentialId: string;      // Base64URL
    transports: string[];
    prfEnabled: boolean;
    residentKeyCreated: boolean;
}

export interface CredentialAssertionResult {
    credentialId: string;      // Base64URL
    prfOutput: string | null;  // Base64 of PRF first result
}

export async function createCredential(
    optionsJson: string
): Promise<CredentialCreationResult>

export async function getCredential(
    optionsJson: string
): Promise<CredentialAssertionResult>
```

**C# binding:**
```csharp
public interface INavigatorCredentialsBinding
{
    Task<CredentialCreationResult> CreateAsync(PublicKeyCredentialCreationOptions options);
    Task<CredentialAssertionResult> GetAsync(PublicKeyCredentialRequestOptions options);
}
```

### Phase 3: Migrate WebauthnService Business Logic

**Files to modify:**
- `Extension/Services/WebauthnService.cs`
- `Extension/Services/IWebauthnService.cs`

**Key changes:**
1. Replace `IJSObjectReference interopModule` with `INavigatorCredentialsBinding`
2. Move salt derivation to C#:
   ```csharp
   private async Task<byte[]> DeriveSaltFromProfileIdAsync()
   {
       var profileId = await GetOrCreateProfileIdentifierAsync();
       return await _cryptoService.Sha256Async(Encoding.UTF8.GetBytes(profileId));
   }
   ```
3. Move key derivation to C#:
   ```csharp
   private byte[] DeriveEncryptionKey(string profileId, byte[] prfOutput)
   {
       // SHA256(profileId || prfOutput || "KERI Auth")
       return _cryptoService.DeriveKeyFromPrf(profileId, prfOutput);
   }
   ```
4. Move encrypt/decrypt to C#:
   ```csharp
   private static readonly byte[] FixedNonce = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

   public async Task<byte[]> EncryptAsync(byte[] key, byte[] plaintext)
   {
       return await _cryptoService.AesGcmEncryptAsync(key, plaintext, FixedNonce);
   }
   ```

### Phase 4: Enhanced Authentication UX

Address your concern about better UX during verification flow:

**Current limitation**: The TypeScript uses broad transport hints `['usb', 'nfc', 'ble', 'internal', 'hybrid']`.

**Enhancement**: Store and use known transports per credential:
```csharp
public record RegisteredAuthenticator
{
    // ... existing fields ...
    public required string[] Transports { get; init; }  // Store actual transports from registration
}

// During authentication, use stored transports for faster/better UX
var options = new WebAuthnAuthenticationOptions
{
    AllowedCredentialIds = credentialIds,
    Transports = storedTransports,  // Use actual transports, not all possible
    UserVerification = "preferred"
};
```

**Additional UX improvements possible in C#:**
- Timeout configuration per authentication type
- Better error messages mapped from WebAuthn error codes
- Platform detection to suggest appropriate authenticator types

### Phase 5: Cleanup

**Files to delete:**
- `scripts/modules/src/webauthnCredentialWithPRF.ts`
- `Extension/wwwroot/scripts/es6/webauthnCredentialWithPRF.js` (compiled output)

**Files to modify:**
- `Extension/wwwroot/app.ts` - Remove static import
- `scripts/modules/package.json` - Update if needed
- `Extension/Extension.csproj` - Update if needed

## Testing Strategy

**Manual browser testing checklist:**
1. Register new authenticator (platform authenticator)
2. Register new authenticator (cross-platform/security key)
3. Authenticate with registered authenticator
4. Verify PRF-derived encryption key produces same result
5. Encrypt/decrypt passcode roundtrip
6. Test with multiple browser profiles (profile identifier isolation)
7. Test error cases (user cancellation, timeout, unsupported authenticator)

**Migration verification:**
- Register authenticator with OLD implementation
- Authenticate with NEW implementation (should decrypt passcode correctly)
- This proves backward compatibility of the encryption/key derivation

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| AES-GCM nonce handling differs | Keep fixed nonce identical; test roundtrip |
| Base64/Base64URL encoding differences | Use consistent encoding; test edge cases |
| PRF output extraction fails | Keep minimal JS shim; thoroughly test |
| Blazor.SubtleCrypto library issues | Fallback to manual IJSRuntime calls to SubtleCrypto |

## Dependencies

**NuGet packages:**
- `Blazor.SubtleCrypto` (v9.0.0) - SubtleCrypto wrapper for Blazor WASM

**No changes to:**
- signify-ts integration (completely separate)
- libsodium usage in signify-ts
- Existing storage models (except adding ProfileIdentifierModel)

## Design Decisions (Resolved)

1. **Crypto library**: Blazor.SubtleCrypto (v9.0.0) for AES-GCM only; native .NET for SHA-256 and key derivation.

2. **Key derivation**: Use `SHA256(PRF output || "KERI Auth")` instead of HKDF. Simpler, native .NET, no JS interop needed.

3. **Fixed nonce**: Keep as-is with light documentation comment. The fixed nonce is safe because keys are derived fresh from PRF each time (never reused).

4. **credProps extension**: Verifying resident key support (`credProps.rk === true`) remains essential.

5. **Backward compatibility**: No backward compatibility needed for authenticators registered with the old TypeScript implementation. Users will need to re-register authenticators after migration.

6. **Schema version**: Add a `SchemaVersion` field to `RegisteredAuthenticator` model for future compatibility checks. No migration logic needed for initial implementation.

7. **Old registration handling**: Silently ignore old registrations (they'll fail to decrypt with the new key derivation). No automatic clearing or user prompts.

8. **TypeScript shim location**: `scripts/modules/src/navigatorCredentialsShim.ts` (no external dependencies needed).

9. **Transport storage**: Store the transports returned by `AuthenticatorAttestationResponse.getTransports()` in the `RegisteredAuthenticator` record. This enables per-credential transport hints during authentication, improving UX by reducing unnecessary prompts.
   - If `getTransports()` returns empty/undefined, fallback to inferring from `authenticatorAttachment` option used during registration:
     - `"platform"` → `["internal"]`
     - `"cross-platform"` → `["usb", "nfc", "ble", "hybrid"]`
     - unspecified → `["usb", "nfc", "ble", "internal", "hybrid"]` (all transports)

10. **Preferences transport settings**: The `Preferences.SelectedTransportOptions` and `Preferences.AuthenticatorTransports` settings become **obsolete** once per-credential transports are stored. During authentication, use the union of stored transports from all registered authenticators instead of user preferences. These preference fields can be deprecated in a future release.

## Timeline Estimate

| Phase | Effort |
|-------|--------|
| Phase 1: Infrastructure | 4-6 hours |
| Phase 2: Navigator Binding | 3-4 hours |
| Phase 3: Service Migration | 6-8 hours |
| Phase 4: UX Enhancements | 2-4 hours |
| Phase 5: Cleanup | 1-2 hours |
| Testing & Validation | 4-6 hours |
| **Total** | **20-30 hours** |

## References

- [WebAuthn PRF Extension Explainer](https://github.com/w3c/webauthn/wiki/Explainer:-PRF-extension)
- [BlazorPRF Project](https://github.com/b-straub/BlazorPRF) - Similar implementation for reference
- [SpawnDev.BlazorJS.Cryptography](https://github.com/LostBeard/SpawnDev.BlazorJS.Cryptography)
- [Blazor WASM Crypto Limitations](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/5.0/cryptography-apis-not-supported-on-blazor-webassembly)
- [Blazor WASM PublicKeyCredential Issue](https://github.com/dotnet/aspnetcore/issues/45236)
