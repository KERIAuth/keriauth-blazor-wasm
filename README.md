# KERI Auth Browser Extension
[![KERI Auth build](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml/badge.svg)](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml)
# Overview
**KERI Auth** is a browser extension designed to accelerate adoption of establishing secure and authentic trust between an individual and website they visit, based on the emerging standards and implementations for decentralized key management and identifiers (KERI), verifiable credentials (ACDC), and roots of trust such as GLEIF's vLEI. 

Together, this comprehensive solution is aimed at greatly reducing security and privacy vulnerabilities with today’s set of internet mechanisms (e.g., federated identity, passwords, SMS 2FA, certificate authority processes, shared secrets, access tokens, and DNS).

The KERI Auth 1.0 release is under development and targets the features below.

# Features
From the end user’s perspective, the extension enables the user to create and manage their own stable identifiers (KERI AIDs) with signing keys, and credentials. It can utilize credentials issued by the website owner and/or other issuers they trust.
V1.0 target features include:
* Create a random passcode
* Configure a connection with a KERI Agent Service of your choosing
* Create one or more KERI identifiers
* Select your current KERI identifier
* View credentials issued to your selected identifier
* Visit websites supporting KERI's Polaris-Web protocol, and:
  * Launch the extension
  * Authenticate ("sign in") with your KERI identifier
  * Authorize ("sign in") with a credential you've received
  * Sign Http Header Requests to access web resources
  * Addditional signing interactions
* Optionally configure a Webauthn compliant authenticator such as a hardware key to unlock KERI Auth

Product Roadmap goals will evolve and may include interoperability with other KERI-related extensions and website JavaScript APIs.
For more information, see [https://keriauth.com](https://keriauth.com) and other content below.

<hr/>

# Intro For Users

## Installation
The most recent stable version is available from the [Chrome Web Store](https://chromewebstore.google.com/search/keri%20auth). Or, you can test the most recent [GitHub build action artifact](https://github.com/KERIAuth/keriauth-blazor-wasm/actions?query=is%3Acompleted+branch%3Amain) or your own local build.

## Runtime Dependencies
To successfully install and use the KERI Auth browser extension, you need the following:
* **Chromium-based Browser** minimum version as specified in the manifest.json file. Browsers supported include Chrome, Edge, and Brave.
* **Web page supporting Polaris-web**, a JavaScript API protocol
* **Connection to KERI Agent Service**. [KERIA]((https://github.com/weboftrust/keria)) is a multi-tenant service that provides infrastructure for one or more Signify clients such as the KERI Auth browser extension.
Over time, we expect many KERIA service providers to be available, including a turn-key self-hosted option.
KERIA creates a separate agent instance for each Signify client as well as partitions the server’s storage to isolate agent instances from each other.
The KERIA agent instance does not hold any of the user’s signing keys, as signing is the job for a Signify client, which is KERI Auth in our case.
The agent instance does hold ACDCs (which may contain PII) as it needs to verify KEL-backed signatures and does exchange messages with other agents on behalf of the AID’s controller.

<hr/>

# Intro For Developers
## Architecture

### Illustration of Primary Components
![KERI Auth Architecture](KERIAuthArchitecture.jpg)
Figure: KERI Auth Browser Extension Architecture ([source](https://docs.google.com/drawings/d/1xICKkvaJkS4IrOGcj_3GidkKHu1VcIrzGCN_vJSvAv4))

### Manifest.json
* Describes the extension and its minimum permissions. Additional permissions, such as to interact with a specific website, are requested of the user during use.

### Browser Extension Action Icon/Button
* The Action button and its context menu appear after install in the upper-right corner of the browser after the user pins the extension from Chrome's Extensions menu.
* The Action button is used primarily to indicate the user’s intent and permission to interact with the active browser tab and its website in the future.

### BackgroundWorker (aka Service-worker)
* Runs background tasks.
* Sends and handles messages to/from the webpage via the extension's content script.
* Sends and handles messages to/from the WASM App.
* Persists configuration via chrome.storage.
* Communicates with KERIA agent via signify-ts library.

### Web Page and Content Script Interaction
#### Content Script
* With the user’s permission this script is injected into the active web page after the user initiates the action.
* Runs in an isolated JavaScript context
* Handles messages to/from the website via a JavaScript API (polaris-web).
* Handles messages to/from the BackgroundWorker.

#### Web Page
* Provided by a website owner, leveraging JavaScript interfaces defined in polaris-web, which interacts with the Content Script.
* Interacts with extension via content script to get user's choice of Identifier (KERI AID) and Credential (ACDC).
* Responsible for validating and determining acceptance of presented Identifier and Credential. May use other services for validation of the identifier's key-state, credential schema, and issuer's root of trust.

## Additional Project Resources

* [CLAUDE.md](claude.md)

# Acknowledgments and References
* Components and Libraries
  * [BrowserExtension](https://github.com/mingyaulee/Blazor.BrowserExtension) by mingyaulee
  * [signify-ts](https://github.com/webOfTrust/signify-ts/) by WebOfTrust
  * [polaris-web](https://github.com/WebOfTrust/polaris-web) by WebOfTrust

* KERI Agent Service
  * [keria](https://github.com/WebOfTrust/keria) by WebOfTrust

* Technical Training
  * [vlei-trainings](https://github.com/gleif-IT/vlei-trainings) by GLEIF

* Related Identity Wallets
  * [Veridian Wallet](https://github.com/cardano-foundation/cf-identity-wallet) by Cardano Foundation and Veridian.id
  * [Signify Browser Extension](https://github.com/WebOfTrust/signify-browser-extension) by WebOfTrust
* Legal Entity Roots of Trust and Credential Verification
  * [Verifiable Legal Entity Identifier (vLEI)](https://www.gleif.org/en/organizational-identity/introducing-the-verifiable-lei-vlei) by Global Legal Entity Identifier Foundation (GLEIF)
  * [Qualified vLEI Issuers (QVIs)](https://www.gleif.org/en/organizational-identity/get-a-vlei-list-of-qualified-vlei-issuing-organizations) by GLEIF
  * [vlei-verifier](https://github.com/GLEIF-IT/vlei-verifier) by GLEIF

<!-- TODO P2 See acknowledgements file for other 3rd parties utilized -->


