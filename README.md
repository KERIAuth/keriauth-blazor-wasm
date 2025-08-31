# KERI Auth Browser Extension
[![KERI Auth build](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml/badge.svg)](https://github.com/keriauth/keriauth-blazor-wasm/actions/workflows/dotnet.yml)
# Overview
**KERI Auth** is a browser extension designed to accelerate adoption of establishing secure and authentic trust between an individual and website they visit, based on the emerging standards and implementations for decentralized key management and identifiers (KERI), verifiable credentials (ACDC), and roots of trust such as GLEIF's vLEI.

These solutions are aimed at greatly reducing security and privacy vulnerabilities with today’s set of internet mechanisms (e.g., federated identity, passwords, SMS 2FA, certificate authority processes, shared secrets, access tokens, and DNS).

The KERI Auth 1.0 release is under development and targets the features below.

# Features
From the end user’s perspective, the extension enables the user to create and manage their own stable identifiers (KERI AIDs), signing keys, and credentials. It can utilize credentials issued by the website owner and/or other issuers they trust.
V1.0 target features include:
* Configure a connection with a KERI Agent Service of your choosing
* Create one or more KERI identifiers
* Select your current KERI identifier
* View credentials issued to your identifiers
* Visit websites supporting KERI's Signify protocol, and:
  * Launch the extension
  * Authenticate ("sign in") with your KERI identifier
  * Authorize ("sign in") with a credential you've received
  * Sign Http Header Requests to access web resources

Product Roadmap goals will evolve and may include interoperability with other KERI-related extensions and website JavaScript APIs.
For more information, see [https://keriauth.com](https://keriauth.com), GitHub Issues, and join the [community](#community) discussions.

<hr/>

# For Users

## Installation
The extension will eventually be available for installation from the Chrome Web Store. Until then, please contact us for the latest pre-release and instructions.

## Runtime Dependencies
To successfully install and use the KERI Auth browser extension, you need the following:
* **Chromium-based Browser** version 127 (released July 2024) or later. Chrome, Edge, or Brave.
* **Web page supporting Polaris-web**, a JavaScript API protocol
* **Connection to KERI Agent Service**. [KERIA]((https://github.com/weboftrust/keria)) is a multi-tenant service that provides infrastructure for one or more Signify clients such as the KERI Auth browser extension.
Over time, we expect many KERIA service providers to be available, including a turn-key self-hosted option. There is currently no turn-key solution to set up a KERIA service along with configured Witness services and Watcher network.
KERIA creates a separate agent instance for each Signify client as well as partitions the server’s storage to isolate agent instances from each other.
The KERIA agent instance does not hold any of the user’s signing keys, as signing is the job for a Signify client.
The agent instance does hold ACDCs (which may contain PII) as it needs to verify KEL-backed signatures and does exchange messages with other agents on behalf of the AID’s controller.

<hr/>

# For Developers
## Architecture

This diagram depicts the primary components of the solution and how they interact. The sections below provide more detail on the components.
![KERI Auth Architecture](KERIAuthArchitecture.jpg)
Figure: KERI Auth Browser Extension Architecture ([source](https://docs.google.com/drawings/d/1xICKkvaJkS4IrOGcj_3GidkKHu1VcIrzGCN_vJSvAv4))

### Manifest.json
* Describes the extension and its permissions.

### Browser Extension Action Icon/Button
* Action button and its context menu appear after install in the upper-right corner of the browser.
* Used to indicate the user’s intent and permission to interact with the current browser page.

### Service-worker
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
* Handles messages to/from the service-worker.

#### Web Page
* Provided by a website owner, leveraging JavaScript interfaces defined in polaris-web, which interacts with the Content Script.
* Interacts with extension via content script to get user's choice of Identifier (KERI AID) and Credential (ACDC).
* Responsible for validating and determining acceptance of presented Identifier and Credential. May use other services for validation of the identifier's key-state, credential schema, and issuer's root of trust, such as GLEIF's [vlei-verifier](https://github.com/GLEIF-IT/vlei-verifier)

### Blazor WASM App Components

#### Single Page App and Its Pages
* Bootstrapped by index.html
* Program.cs, App.wasm, Razor pages, and services that run as WASM when any non-trivial extension UI is visible.
* The app and its pages can be hosted within the browser's Action Popup or a tab.

#### Services
* Interact with service-worker (and indirectly the web pages) via messages.
* Communicates with KERIA service via the signify-ts library.
* Persists configuration and notifications via chrome.storage.

### Design security considerations
The following rules are enforced by design to ensure the security of the extension:
* Only sends signed Http Header Requests ("headers") to the website if they are safe (e.g. GET) or user approves them (e.g., POST).
* Caches the passcode only temporarily and clears it from cache after a max of 5 minutes of inactivity.
* Only accepts content script messages from the active tab website, during authentication or after a signing association exists.
* Declare minimum required and optional permissions in the extension's manifest.
* Never runs dynamic or inline scripts.
* Assures all sensitive data (e.g., passcode) never reaches the content script or website.

## Development Setup and Build
* Prerequisites (Node.js, npm, browser development tools, etc.).
* Clone the repository and build the extension.

## Extension Installation
* After building from source code, use Chrome/Edge/Brave's "developer mode" and press `load unpacked`, selecting the directory KeriAuth.BrowserExtension\bin\Release\net9.0\browserextension.

<hr/>

# Acknowledgments
We especially appreciate the contributors of following libraries we use:
* [WebOfTrust/signify-ts](https://github.com/webOfTrust/signify-ts/) by WebOfTrust
* [gleif-IT/vlei-trainings](https://github.com/gleif-IT/vlei-trainings) by GLEIF
* [mingyaulee/Blazor.BrowserExtension](https://github.com/mingyaulee/Blazor.BrowserExtension) by mingyaulee
<!-- TODO P2 See acknowledgements file for other 3rd parties utilized -->

<hr/>

# Community
Join the project's community on [Blocktrust's Discord](https://discord.gg/Va79ag9RCw), or via Trust Over IP Foundation projects.

<hr/>

# Highlighted References
## Articles
* [Introduction to KERI](https://medium.com/finema/the-hitchhikers-guide-to-keri-part-1-51371f655bba)
* [vLEI Demystified Part 1: Comprehensive Overview](https://medium.com/finema/vlei-demystified-part-1-comprehensive-overview-212349c09643)

## Related Repositories
* [WebOfTrust/signify-ts](https://github.com/WebOfTrust/signify-ts)
* [WebOfTrust/polaris-web](https://github.com/WebOfTrust/polaris-web)
* [WebOfTrust/signify-browser-extension](https://github.com/WebOfTrust/signify-browser-extension)
* [WebOfTrust/keria](https://github.com/WebOfTrust/keria)
* [cardano-foundation/cf-identity-wallet](https://github.com/cardano-foundation/cf-identity-wallet)