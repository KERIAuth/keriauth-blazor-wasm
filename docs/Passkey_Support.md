# Passkey Support

The Dign Wallet leverages the Webauthn protocol to register and configure webauthn credentials on authenticators. These are marketed as "passkeys", although not all passkeys are of the same security!

## What do I need?

You'll need an authenticator that supports the CTAP 2.1 standard with the PRF (Pseudo-Random Function) extension. This includes:

### Supported Authenticators

- **Hardware security keys** (e.g., YubiKey 5 series with firmware 5.2+, Google Titan)
- **Google Password Manager** (Chrome platform passkey with PRF support)

### Unsupported Authenticators

The following authenticators do **not** currently support the PRF extension required by the extension:

- **Windows Hello** - Does not support PRF extension
- **Apple Touch ID / Face ID** - Does not support PRF extension
- **iCloud Keychain** - Does not support PRF extension

## How does this work?

When you register an authenticator with the extension:

1. **The extension interacts with the authenticator**, sending it data unique to your browser profile (a deterministic salt derived from your KERIA connection and other data).

2. **The authenticator uses its Pseudo-random Function (PRF)** to generate a unique cryptographic output. This output is derived from:
   - The unique data it received
   - Hardware-specific key material internal to the authenticator

3. **The extension derives an encryption key** from the PRF output. This encryption key is:
   - Never stored anywhere
   - Unique to the combination of: your browser profile + the extension + the specific registered authenticator

4. **Your passcode is encrypted/decrypted** using this derived encryption key, and only the encrypted passcode is stored securely with your Chromium's extension storage.

5. **On subsequent unlocks**, the same PRF process is repeated to derive the same encryption key, allowing the extension to decrypt your passcode that lives only temporarily in its memory.

## Important Security Note

Your passcode is **never stored directly** by the extension or any authenticator. Only the encrypted version is kept in your browser profile.

**You must continue to keep your passcode safely stored** in the event:
- An authenticator becomes unavailable or is lost
- The browser profile is reset or deleted
- The extension is reinstalled

Without your passcode, you cannot recover access if all registered authenticators become unavailable.

## WebAuthn Settings Reference

The specific settings you can choose in the user interface when you create a "passkey" affect the security-vs-convenience tradeoffs among authenticator providers. In general, only the option to use a hardware authenticator from a reputable vendor is known to be secure.

### Registration Settings

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
