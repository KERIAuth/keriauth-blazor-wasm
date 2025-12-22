# Passkey Support in KERI Auth

## What do I need?

You'll need an authenticator that supports the CTAP 2.1 standard with the PRF (Pseudo-Random Function) extension. This includes:

### Supported Authenticators

- **Hardware security keys** (e.g., YubiKey 5 series with firmware 5.2+, Google Titan)
- **Google Password Manager** (Chrome platform passkey with PRF support)

### Unsupported Authenticators

The following authenticators do **not** currently support the PRF extension required by KERI Auth:

- **Windows Hello** - Does not support PRF extension
- **Apple Touch ID / Face ID** - Does not support PRF extension
- **iCloud Keychain** - Does not support PRF extension

## How does this work?

When you register an authenticator with KERI Auth:

1. **KERI Auth interacts with the authenticator**, sending it data unique to your browser profile (a deterministic salt derived from your profile ID).

2. **The authenticator uses its Pseudo-random Function (PRF)** to generate a unique cryptographic output. This output is derived from:
   - The unique salt data it received
   - Hardware-specific key material internal to the authenticator

3. **KERI Auth derives an encryption key** from the PRF output. This key is:
   - Never stored anywhere
   - Unique to the combination of: browser profile + KERI Auth + the specific registered authenticator

4. **Your passcode is encrypted** using this derived key and stored securely in your browser profile, accessible only to the extension.

5. **On subsequent unlocks**, the same PRF process is repeated to derive the same encryption key, allowing KERI Auth to decrypt your passcode.

## Important Security Note

Your passcode is **never stored directly** by KERI Auth or any authenticator. Only the encrypted version is kept in your browser profile.

**You must continue to keep your passcode safely stored** in the event:
- An authenticator becomes unavailable or is lost
- The browser profile is reset or deleted
- The KERI Auth extension is reinstalled

Without your passcode, you cannot recover access if all registered authenticators become unavailable.

## WebAuthn Settings Reference

### Registration Settings for Different Authenticator Types

| Setting | Hardware Security Key | Google Password Manager |
|---------|----------------------|------------------------|
| `authenticatorAttachment` | `cross-platform` | `platform` |
| `residentKey` | `required` | `required` |
| `userVerification` | `required` | `required` |
| `attestation` | `direct` | `none` |
| `hints` | `["security-key"]` | `[]` (empty) |

### Why These Settings Matter

- **`authenticatorAttachment`**: Determines whether to use a roaming authenticator (USB key) or platform authenticator (built-in to device/browser)
- **`residentKey: required`**: Ensures the credential is discoverable (a "passkey")
- **`userVerification: required`**: Requires user verification (PIN, fingerprint, etc.) for security
- **`attestation: direct`**: Requests full attestation from hardware keys to identify the authenticator model (provides AAGUID)
- **`attestation: none`**: Platform authenticators like GPM don't typically provide attestation

## References

- [WebAuthn Level 3 Specification](https://www.w3.org/TR/webauthn-3/)
- [Discoverable Credentials Deep Dive (web.dev)](https://web.dev/articles/webauthn-discoverable-credentials)
- [WebAuthn PRF Extension (Chromium)](https://groups.google.com/a/chromium.org/g/blink-dev/c/iTNOgLwD2bI)
- [Securing WebAuthn with Attestation (Yubico)](https://developers.yubico.com/WebAuthn/Concepts/Securing_WebAuthn_with_Attestation.html)
