# KERI Auth Identity Wallet
[![KERI Auth build](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml/badge.svg)](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml)

## Overview
**KERI Auth** is an identity wallet packaged as a Chromium browser extension, designed to accelerate adoption of establishing secure and authentic trust between individuals and websites they visit. It is based on standards for cryptographic and legal roots of trust: decentralized key management and identifiers (KERI), verifiable credentials (ACDC), as GLEIF's Verifiable Legal Entity Identifier (vLEI) ecosystem.

This solution aims to greatly reduce security and privacy vulnerabilities with today's internet mechanisms (e.g., federated identity, passwords, SMS 2FA, certificate authority processes, shared secrets, access tokens, and DNS).

The KERI Auth 1.0 release is under development.

## Features
From the end user's perspective, the extension enables the user to create and manage their own stable identifiers (KERI AIDs) with signing keys, and credentials. V1.0 target features include:
* Create a random passcode
* Configure a connection with a KERIA Cloud Service of your choosing
* Create one or more KERI identifiers
* Select your current KERI identifier
* View credentials issued to your selected identifier
* Visit websites supporting KERI's Polaris-Web protocol, and:
  * Launch the extension
  * Authenticate ("sign in") with your KERI identifier
  * Authorize ("sign in") with a credential you've received
  * Sign HTTP Header Requests to access web resources
  * Additional signing interactions
* Register a passkey on a WebAuthn compliant authenticator such as a hardware key to unlock KERI Auth

Product roadmap goals will evolve and may include interoperability with other KERI-related extensions and website JavaScript APIs.

For more information, see [https://keriauth.com](https://keriauth.com).

## Installation
The most recent stable version is available from the [Chrome Web Store](https://chromewebstore.google.com/search/keri%20auth). Or, you can test the most recent [GitHub build action artifact](https://github.com/KERIAuth/keriauth-blazor-wasm/actions?query=is%3Acompleted+branch%3Amain) or your own local build.

### Runtime Dependencies
* **Chromium-based Browser** minimum version as specified in manifest.json (Chrome, Edge, or Brave)
* **Web page supporting Polaris-web**, a JavaScript API protocol
* **Connection to KERIA Cloud Service** — [KERIA](https://github.com/weboftrust/keria) is a multi-tenant service that provides infrastructure for Signify clients. KERIA creates a separate agent instance for each client and partitions storage to isolate agents. The agent does not hold signing keys; signing is performed by the Signify client (KERI Auth). The agent does hold ACDCs and exchanges messages with other agents on behalf of the AID's controller.

## For Developers

* [ARCHITECTURE.md](docs/ARCHITECTURE.md) — system structure and component overview
* [BUILD.md](docs/BUILD.md) — build instructions, prerequisites, and troubleshooting
* [CODING.md](docs/CODING.md) — C#, TypeScript, and interop coding standards
* [CLAUDE.md](CLAUDE.md) — directives for Claude Code AI assistant

## License

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.

## Trademark Notice

"KERI Auth" and associated logos are trademarks of LivelyGig LLC.
Use of these trademarks is subject to the [Trademark Policy](branding/trademark-policy.md).
Forked or modified versions must not use the "KERI Auth" name or logo without permission.

## Acknowledgments and References
* Components and Libraries
  * [BrowserExtension](https://github.com/mingyaulee/Blazor.BrowserExtension) by mingyaulee
  * [signify-ts](https://github.com/webOfTrust/signify-ts/) by WebOfTrust
  * [polaris-web](https://github.com/WebOfTrust/polaris-web) by WebOfTrust

* KERIA Cloud Service
  * [keria](https://github.com/WebOfTrust/keria) by WebOfTrust

* Technical Training
  * [vlei-hackathon-2025-workshop](https://github.com/GLEIF-IT/vlei-hackathon-2025-workshop)
  * [vlei-trainings](https://github.com/gleif-IT/vlei-trainings) by GLEIF

* Other Notable KERI Open Source Identity Wallets
  * [Veridian Wallet](https://veridian.id/)
* Legal Entity Roots of Trust and Credential Verification
  * [Verifiable Legal Entity Identifier (vLEI)](https://www.gleif.org/en/organizational-identity/introducing-the-verifiable-lei-vlei) by Global Legal Entity Identifier Foundation (GLEIF)
  * [Qualified vLEI Issuers (QVIs)](https://www.gleif.org/en/organizational-identity/get-a-vlei-list-of-qualified-vlei-issuing-organizations) by GLEIF
  * [vlei-verifier](https://github.com/GLEIF-IT/vlei-verifier) by GLEIF

<!-- TODO P2 See acknowledgements file for other 3rd parties utilized -->
