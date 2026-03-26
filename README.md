# DIGN Identity Wallet
[![KERI Auth build](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml/badge.svg)](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml)

## Overview
**DIGN** is an identity wallet packaged as a Chromium browser extension, designed to establish secure and authentic trust between individuals and websites they visit. It is based on standards for cryptographic and legal roots of trust: decentralized key management and identifiers (KERI), verifiable credentials (ACDC), and GLEIF's Verifiable Legal Entity Identifier (vLEI) ecosystem. DIGN includes [signify-ts](https://github.com/webOfTrust/signify-ts/) to keep key generation and signing at the edge, while delegating event processing, credential storage, and agent communication to a hosted [KERIA](https://github.com/weboftrust/keria) service that never has access to the controller's private keys.

This solution is part of these broader efforts to prioritize security first (including authenticity, confidential communications, then privacy), unlike much of the internet's infrastructure that continues to operate with known security vulnerabilities (e.g., federated identity, passwords, SMS 2FA, certificate authority processes, shared secrets, and long-lived access tokens). Is "pretty-good security" good enough?

Note that DIGN's features and quality are currently incomplete, and thus it can be considered at the beta stage.

## Features
From the end user's perspective, the extension enables the user to create and control their own stable identifiers (KERI AIDs) with signing keys, and credentials. The target version 1.0 features include:
* Create a random passcode
* Configure a connection with a [KERIA Service](https://github.com/webOfTrust/keria) instance of your choosing
* Create one or more KERI identifiers (called AIDs or profiles)
* Select your active profile
* View credentials issued to your selected identifier
* Connect with other AID controllers via out-of-band introductions (OOBIs), to be used for secure communications.
* Participate in ACDC Credential lifecycles (issue, hold, present, verify)
* Interact with websites supporting KERI's [Polaris-Web](https://github.com/WebOfTrust/polaris-web) and other message protocols, and:
  * Launch the extension
  * Present your KERI Identifier (AID/profile) or Credential to "sign in" to the website
  * Sign HTTP Header Requests to access web resources
  * Sign additional interactions
* Register a passkey on a WebAuthn-compliant authenticator such as a hardware key to unlock DIGN
* ... and more.

## Installation
The most recent stable version is available from the [Chrome Web Store](https://chromewebstore.google.com/search/dign). Or, you can test with the most recent [GitHub build action artifact](https://github.com/KERIAuth/keriauth-blazor-wasm/actions?query=is%3Acompleted+branch%3Amain), or from your own local build.

## Runtime Dependencies
* **Chromium-based Browser** minimum version as specified in manifest.json (Chrome, Edge, or Brave)
* **Web page supporting Polaris-web**, a JavaScript API protocol
* **Connection to a KERIA Service** — [KERIA](https://github.com/weboftrust/keria) is a multi-tenant agent service that provides infrastructure for Signify clients. KERIA creates a separate agent instance for each client and partitions storage to isolate agents. Key generation and event signing are performed at the edge by the Signify client (such as DIGN) using signify-ts; KERIA stores only encrypted key material and salts that it cannot decrypt — decryption keys never leave the client. KERIA handles event generation, event validation, credential storage, and message exchange with other agents. When DIGN first connects, it establishes a Client AID and KERIA creates a delegated Agent AID anchored to it — a formal KERI delegation where the agent's authority is cryptographically bound to the client. The client signs all requests using its Client AID keys; the agent signs all responses with its own Agent AID keys, creating a mutually authenticated channel. The agent acts on behalf of the controller's managed AIDs (identifiers/profiles) for event processing, credential workflows, and peer communication, but the controller retains exclusive signing authority — the agent cannot sign as the controller.

## For Developers

* [ARCHITECTURE.md](docs/ARCHITECTURE.md) — system structure and component overview
* [BUILD.md](docs/BUILD.md) — build instructions, prerequisites, and troubleshooting
* [CODING.md](docs/CODING.md) — C#, TypeScript, and interop coding standards

## License

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.

## Trademark Notice

"DIGN" and "KERI Auth" and associated logos are trademarks of LivelyGig LLC.
Use of these trademarks is subject to the [Trademark Policy](branding/trademark-policy.md).
Forked or modified versions of this repository must not use the trademark names or logos without permission.

## Code Acknowledgments and References
* Components and libraries
  * [BrowserExtension](https://github.com/mingyaulee/Blazor.BrowserExtension) by mingyaulee
  * [WebExtensions.Net](https://github.com/mingyaulee/WebExtensions.Net) by mingyaulee
  * [signify-ts](https://github.com/webOfTrust/signify-ts/) by WebOfTrust
  * [polaris-web](https://github.com/WebOfTrust/polaris-web) by WebOfTrust

* KERIA (KERI Agent) Service
  * [KERIA](https://github.com/WebOfTrust/keria) by WebOfTrust

* Technical Training
  * [vlei-hackathon-2025-workshop](https://github.com/GLEIF-IT/vlei-hackathon-2025-workshop) and [vlei-trainings](https://github.com/gleif-IT/vlei-trainings) by GLEIF

* Other Notable KERI Open-source Identity Wallets
  * [Veridian Wallet](https://veridian.id/)

* Legal Entity Roots of Trust and Credential Verification
  * [Verifiable Legal Entity Identifier (vLEI)](https://www.gleif.org/en/organizational-identity/introducing-the-verifiable-lei-vlei) by Global Legal Entity Identifier Foundation (GLEIF)
  * [Qualified vLEI Issuers (QVIs)](https://www.gleif.org/en/organizational-identity/get-a-vlei-list-of-qualified-vlei-issuing-organizations) by GLEIF
  * [vlei-verifier](https://github.com/GLEIF-IT/vlei-verifier) by GLEIF

<!-- TODO P2 See acknowledgements file for other 3rd parties utilized -->
