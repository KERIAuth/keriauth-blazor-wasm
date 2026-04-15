# Extension Manual Test Case Outline

**Note**: Log messages generated from WASM code (including BackgroundWorker.cs and all C# services) depend on the log levels configured in `Extension/wwwroot/appsettings.json`. For manual testing, set relevant categories (e.g., `Extension.BackgroundWorker`, `Extension.Services`) to `"Debug"` or `"Information"` to see detailed log output in the service worker console.

## Table of Contents

- [Test Environment Setup](#test-environment-setup)
  - [OS](#os)
  - [Browser](#browser)
  - [Browser Settings](#browser-settings)
  - [Extension Install Source / Variant](#extension-install-source--variant)
  - [KERIA Server](#keria-server)
  - [Polaris-Web Pages](#polaris-web-pages)
- [Install / Update / Uninstall](#install--update--uninstall)
  - [Fresh Install](#fresh-install)
  - [Update](#update)
    - [With version change](#with-version-change)
    - [With updated Privacy Policy and/or Terms of Use](#with-updated-privacy-policy-andor-terms-of-use)
    - [Without version change](#without-version-change)
    - [Update via app store](#update-via-app-store)
  - [Uninstall](#uninstall)
- [Simulated Page Requests](#simulated-page-requests)
  - [Sign In with Identifier (AID) selection](#sign-in-with-identifier-aid-selection)
  - [Sign In with Credential selection](#sign-in-with-credential-selection)
  - [Sign In (user chooses method)](#sign-in-user-chooses-method)
  - [Sign HTTP Request Headers](#sign-http-request-headers)
  - [Sign Data](#sign-data)
    - [Simple](#simple)
    - [Customized](#customized)
    - [Customized, partial override](#customized-partial-override)
  - [Create Data Attestation Credential](#create-data-attestation-credential)
  - [Connection Invite](#connection-invite)
  - [IPEX](#ipex)
    - [IPEX Terminology: Issuance](#ipex-terminology-issuance)
    - [IPEX Apply (Credential Issuance) for an ECR](#ipex-apply-credential-issuance-for-an-ecr)
    - [Offer / Grant Credential from Apply Notification (Multi-Step Dialog)](#offer--grant-credential-from-apply-notification-multi-step-dialog)
    - [Grant Credential from Agree Notification (Review-Agree Dialog)](#grant-credential-from-agree-notification-review-agree-dialog)
    - [CreateTestDataPage — Issue Credentials Panel (Ad-Hoc)](#createtestdatapage--issue-credentials-panel-ad-hoc)
    - [IPEX Apply (Credential Presentation)](#ipex-apply-credential-presentation)
    - [IPEX Agree (Credential Issuance)](#ipex-agree-credential-issuance)
    - [IPEX Agree (Credential Presentation)](#ipex-agree-credential-presentation)
    - [IPEX Grant (Credential Issuance)](#ipex-grant-credential-issuance)
    - [IPEX Grant (Credential Presentation)](#ipex-grant-credential-presentation)
    - [IPEX Admit (Credential Issuance)](#ipex-admit-credential-issuance)
    - [IPEX Admit (Credential Presentation)](#ipex-admit-credential-presentation)
    - [Same-Agent Notification Limitation](#same-agent-notification-limitation)
- [Session Expiration Tests](#session-expiration-tests)
  - [A. Inactivity Timeout Without User Activity](#a-inactivity-timeout-without-user-activity)
  - [B. Inactivity Timeout With User Activity](#b-inactivity-timeout-with-user-activity)
  - [C. Session Persistence Across App Restart (Before Timeout)](#c-session-persistence-across-app-restart-before-timeout)
  - [D. Session Expiration During App Closure](#d-session-expiration-during-app-closure)
  - [E. Session Expiration after Browser Close, Restart](#e-session-expiration-after-browser-close-restart)
  - [F. Start App, Exit, Wait, Restart App](#f-start-app-exit-wait-restart-app)
  - [G. Click or Keyboard Activity Resets Expiration Timer](#g-click-or-keyboard-activity-resets-expiration-timer)
- [Notification Polling Tests](#notification-polling-tests)
  - [A. Burst Polling on Connect](#a-burst-polling-on-connect)
  - [B. Service Worker Goes Inactive After Burst](#b-service-worker-goes-inactive-after-burst)
  - [C. Recurring Alarm Wakes Service Worker](#c-recurring-alarm-wakes-service-worker)
  - [D. User Activity Restarts Burst](#d-user-activity-restarts-burst)
  - [E. NotificationsPage Opens Triggers Burst (If Not Active)](#e-notificationspage-opens-triggers-burst-if-not-active)
  - [F. Session Lock Clears Alarm](#f-session-lock-clears-alarm)
  - [G. Alarm Survives Service Worker Restart](#g-alarm-survives-service-worker-restart)
  - [H. Connection Invite Triggers Burst](#h-connection-invite-triggers-burst)
- [Seed KERIA with test data](#seed-keria-with-test-data)
- [Connect with KERIA with existing passcode](#connect-with-keria-with-existing-passcode)
- [Connect with KERIA with new passcode](#connect-with-keria-with-new-passcode)
- [Tab-Extension Permission and Content Script Tests](#tab-extension-permission-and-content-script-tests)
  - [A. Content Script Ping Response](#a-content-script-ping-response)
  - [B. Content Script Ready Message](#b-content-script-ready-message)
  - [C. Fresh Site Permission Grant (Action Button Click)](#c-fresh-site-permission-grant-action-button-click)
  - [D. Pre-Granted Permission (Action Button Click)](#d-pre-granted-permission-action-button-click)
  - [E. Already Active Site (Action Button Click)](#e-already-active-site-action-button-click)
  - [F. Stale Content Script Detection (Action Button Click)](#f-stale-content-script-detection-action-button-click)
  - [G. Icon State on Tab Activation](#g-icon-state-on-tab-activation)
  - [H. Icon State on Page Navigation](#h-icon-state-on-page-navigation)
  - [I. Permission Removal via Context Menu](#i-permission-removal-via-context-menu)
  - [J. Extension Reload Recovery](#j-extension-reload-recovery)
  - [K. Browser Restart Recovery](#k-browser-restart-recovery)
  - [L. Browser Resume from Suspend](#l-browser-resume-from-suspend)
  - [M. New Tab with Permitted URL](#m-new-tab-with-permitted-url)
  - [N. Unsupported URL Schemes](#n-unsupported-url-schemes)
  - [O. Service Worker Startup Initialization](#o-service-worker-startup-initialization)
  - [P. Service Worker Cold-Start Wake via Tab Switch](#p-service-worker-cold-start-wake-via-tab-switch)
  - [Q. Service Worker Cold-Start Wake via Permission Grant](#q-service-worker-cold-start-wake-via-permission-grant)
- [Webauthn Authenticator ("Passkey") Tests](#webauthn-authenticator-passkey-tests)
  - [A. Register and Test Webauthn Authenticator](#a-register-and-test-webauthn-authenticator)
  - [B. Authenticate with Webauthn Authenticator](#b-authenticate-with-webauthn-authenticator)
  - [C. Fail Webauthn Authentication](#c-fail-webauthn-authentication)
  - [D. Remove Webauthn Authenticator](#d-remove-webauthn-authenticator)
  - [E. Fallback to Passcode Authentication](#e-fallback-to-passcode-authentication)
  - [F. Multiple Webauthn Authenticators](#f-multiple-webauthn-authenticators)
  - [G. Webauthn Authenticator on Different Browsers/Devices](#g-webauthn-authenticator-on-different-browsersdevices)
- [Network Resilience Tests](#network-resilience-tests)
  - [A. Browser Offline Detection](#a-browser-offline-detection)
  - [B. KERIA Server Unreachable](#b-keria-server-unreachable)
  - [C. Retry on Transient Network Failure](#c-retry-on-transient-network-failure)
  - [D. Service Worker Dormancy and Network State Recovery](#d-service-worker-dormancy-and-network-state-recovery)
  - [E. Developer State Page Accuracy](#e-developer-state-page-accuracy)
- [Credential Presentation](#credential-presentation)
  - [A. Saidify Verification Harness (Developer Test Page)](#a-saidify-verification-harness-developer-test-page)
  - [B. Credential Presentation Page — Undisclosed SAID Preview](#b-credential-presentation-page--undisclosed-said-preview)
  - [C. Credential Presentation Selector — Fill on Switch](#c-credential-presentation-selector--fill-on-switch)
  - [D. Unified Tree Rendering (Presentation and Non-Presentation Parity)](#d-unified-tree-rendering-presentation-and-non-presentation-parity)
  - [E. Inline Chained Credential Under Matching SaidReference (Display Only)](#e-inline-chained-credential-under-matching-saidreference-display-only)
  - [F. Abbreviate SAIDs Preference](#f-abbreviate-saids-preference)
- Other
  - Run Developer/PrimeData workflows
  - Add Contact with QR and camera scanning, or webpage-initiated flow
  - Grant a credential via ipex from Veridian's cred-issuance-ui

## Test Environment Setup
### OS
- [ ] Windows 11
- [ ] iOS latest stable
### Browser
- [ ] Chrome latest stable
- [ ] Edge latest stable
- [ ] Brave latest stable
### Browser Settings
- [ ] Profile authenticated with Google account
- [ ] Anonymouse profile
- [ ] Incognito
### Extension Install Source / Variant
- [ ] Developer Load via Extension page (Release build type)
- [ ] Chrome Web Store
- [ ] GitHub Action Build
- [ ] Version: __________
### KERIA Server
- [ ] Local KERIA instance.  Version: __________
- [ ] GLEIF Testnet KERIA instance. Version: __________
### Polaris-Web Pages
- [ ] Locally Hosted
- [ ] Polaris-Web-Example
- [ ] Doc-Signing-Web-App
- [ ] sign.globalvlei.com
- [ ] other: __________

## Install / Update / Uninstall
###	Fresh Install
1. Step through onboarding flow beginning with Welcome, ending at Dashboard
	- [ ] Expected: successful onboarding, see Dashboard
### Update
#### With version change
1. Install previous version of the extension
1. Obtain or create a new version of the extension with a higher version number in manifest.json 
1. In Chrome's Extension page, hit Refresh (assuming a developer load)
- [ ] Expected: see Welcome page and New Version info

#### With updated Privacy Policy and/or Terms of Use
1. Prerequisite: Install and onboard a version of the extension
1. Simulate a condition where a new Privacy Policy or ToU exists, by modifying the stored OnboardState.PrivacyAgreedHash or .TosAgreedHash value.
1. Or, create a new version of the extension with updated Privacy Policy and/or Terms of Use
1. Restart the extension
- [ ] Expected: On startup, after the NewRelease page, you should be navigated to Terms page, requiring re-acceptance

#### Without version change
- [ ] Expected: see the Welcome page
		
#### Update via app store
1. Install the old version of the extension from the Chrome Web Store
1. Create a new version of the extension with a later version number in manifest.json
1. Publish the new version to the Chrome Web Store
1. Wait for the update to get approval and propagate (1 hour to days)
1. Start the extension and allow it to run, updating in the background for ~3 minutes
1. Exit and restart the extension
- [ ] Expected: see Welcome page and New Version info

#### Uninstall
- [ ] Expected: Browser opens the configured page for user to provide freedback

# Simulated Page Requests
The following can be pasted into the DevTools console on a page with injected Content Script. These should be tested both with the SidePanel open (to test its navigation to dialog pages) and with the SidePanel not open (so the pop-up is used).

## Sign In with Identifier (AID) selection
```js
window.postMessage({type: '/signify/authorize/aid', requestId: crypto.randomUUID(), payload: {message: 'Please select a profile'}}, window.location.origin);
```

## Sign In with Credential selection
```js
window.postMessage({type: '/signify/authorize/credential', requestId: crypto.randomUUID(), payload: {message: 'Please select a credential'}}, window.location.origin);
```

## Sign In (user chooses method)
```js
window.postMessage({type: '/signify/authorize', requestId: crypto.randomUUID(), payload: {message: 'Please authorize'}}, window.location.origin);
```

## Sign HTTP Request Headers
```js
window.postMessage({type: '/signify/sign-request', requestId: crypto.randomUUID(), payload: {url: 'https://example.com/api/data', method: 'GET'}}, window.location.origin);
```

## Sign Data
### Simple
```js
window.postMessage({type: '/signify/sign-data', requestId: crypto.randomUUID(), payload: {message: 'Please sign this data', items: ['item-to-sign-1', 'item-to-sign-2']}}, window.location.origin);
```

### Customized
```js
window.postMessage({type: '/signify/sign-data', requestId: crypto.randomUUID(), payload: {message: '{"requestTitleText":"Authorize Credential Issuance", "requestText":"Review and approve the set of credential fields below", "itemsLabel":"Credential fields", "buttonText":"Authorize"}', items: ['item-to-sign-1', 'item-to-sign-2']}}, window.location.origin);
```

### Customized, partial override
```js
window.postMessage({type: '/signify/sign-data', requestId: crypto.randomUUID(), payload: {message: '{"requestTitleText":"Approve Document"}', items: ['document-hash-abc123']}}, window.location.origin);
```

## Create Data Attestation Credential
```js
window.postMessage({type: '/signify/credential/create/data-attestation', requestId: crypto.randomUUID(), payload: {schemaSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', credData: {d: '', u: '', i: '', dt: '2025-01-01T00:00:00Z', LEI: '5493001KJTIIGC8Y1R17'}}}, window.location.origin);
```

## Connection Invite
```js
window.postMessage({type: '/KeriAuth/connection/invite', requestId: crypto.randomUUID(), payload: {oobi: 'https://keria-ext.dev.idw-sandboxes.cf-deployments.org/oobi/EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3/agent/EMhtGVe_k0b0NQXqJvJzm5NvhYkggbnLkKNaTtxmOcxe?name=CF%20Credential%20Issuance4'}}, window.location.origin);
```

## IPEX

The sample recipient prefixes below are placeholders. For a successful end-to-end test, replace the `recipient` value with the AID prefix of an established connection of your selected sender AID. Similarly, `schemaSaid`, `offerSaid`, and `grantSaid` must reference real SAIDs known to your KERIA agent.

If the recipient is not a known connection, KERIA will return `400 Bad Request: attempt to send to unknown AID=...`. This confirms the full message flow is working correctly (Page → CS → BW → App dialog → BW → signify-ts → KERIA → error response → CS → Page).

Expected manual test flow:
1. Paste the `window.postMessage(...)` into the page's DevTools console
2. The extension dialog should open (popup or side panel) showing the request details
3. Select a sender AID from the dropdown
4. Click **Approve** (or **Reject** to test cancellation)
5. Check the DevTools console for the response (`/signify/reply` with result or error)

### IPEX Terminology: Issuance

**Issuance** refers to signing a new credential at the sender. This is now a distinct, user-visible step in the Offer and Grant dialogs. When the user clicks "Offer Credential" or "Grant Credential" from a notification, they walk through:

1. **Review** — inspect the apply (or agree) request
2. **Issue** — sign the new credential (spinner, progress)
3. **Review credential** — inspect the signed credential via `CredentialComponent` (Card/Tree view, detail level)
4. **Submit** — send the IPEX offer or grant

The issuance step uses the decoupled `RequestIssueEcrCredential` RPC. The submission step uses `RequestSubmitIpexOffer` or `RequestSubmitIpexGrant`.

### IPEX Apply (Credential Issuance) for an ECR
```js
window.postMessage({
  type: '/KeriAuth/ipex/apply',
  requestId: crypto.randomUUID(),
  payload: {
    schemaSaid: 'EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw',
    recipient: 'EFkSnI87zTv7LPOPZdXjoV52wCChfpUqYt7oGp7CjriJ',
    isPresentation: false,
    attributes: {
      "LEI": "5493001KJTIIGC8Y1R17",
      "personLegalName": "John Smith",
      "engagementContextRole": "Head of Standards"
    }
  }
}, window.location.origin);

```

### Offer / Grant Credential from Apply Notification (Multi-Step Dialog)

1. Prerequisites: Authenticated with ECR Auth credential held, `/exn/ipex/apply` notification received
2. Navigate to Notifications, expand the apply notification

**Offer path:**
3. Click **Offer Credential**

Expected:
- [ ] Step 1 shows credential summary: Schema (ECR), Role, Issuer (AidPrefixDisplay with name or `(ext)`), Holder (same)
- [ ] "Apply request JSON" expandable panel shows the raw apply exn
- [ ] Clicking "Next: Issue credential" shows issuing spinner, advances to step 3
- [ ] Step 3 embeds `CredentialComponent` + gear icon for `CredentialViewPrefsComponent` (collapsed by default)
- [ ] Clicking gear opens Card/Tree toggle and detail-level selector; changes update the credential view live
- [ ] Clicking "Offer credential" submits the offer; success snackbar shows; notification marked as read
- [ ] "Offering…" spinner stops (deadlock regression guard)
- [ ] Clicking "Reject application" in step 1 closes dialog; no issuance, no submission
- [ ] Clicking "Reject" in step 3 (post-issuance) surfaces snackbar "Revocation not yet implemented — issued credential remains in your issuer registry"; no submission

**Grant path:**
3. Click **Grant Credential** — same dialog opens with "Grant credential" title and `CardGiftcard` icon
4. Walk through same 4 steps

Expected: same as Offer path, except final button reads "Grant credential" and submission sends `/exn/ipex/grant` instead of `/exn/ipex/offer`.

### Grant Credential from Agree Notification (Review-Agree Dialog)

1. Prerequisites: Authenticated, `/exn/ipex/agree` notification received (from a prior offer)
2. Navigate to Notifications, expand the agree notification
3. Click **Grant Credential**

Expected:
- [ ] Dialog opens in review-agree mode: title "Grant credential (from agree)"
- [ ] Step 1 shows issuer/holder via `AidPrefixDisplay`, prior offer SAID (elided with tooltip), "Agree message JSON" expander
- [ ] Step label says "Step 1 of 3 · Review agree" (issuance step is skipped — 3 steps total)
- [ ] Clicking "Next: Review credential" fetches the credential from the prior offer exchange
- [ ] Step 2 shows the existing credential via `CredentialComponent` (no new issuance occurred)
- [ ] Confirming "Grant credential" submits the grant via `RequestSubmitIpexGrant` with `agreeSaid`
- [ ] Canceling is a plain close (no revoke snackbar since no credential was issued)

### CreateTestDataPage — Issue Credentials Panel (Ad-Hoc)

1. Prerequisites: Authenticated with at least one local AID holding an ECR Auth credential
2. Navigate to CreateTestData page

Expected:
- [ ] A new `<MudExpansionPanel>` titled **"Issue Credentials"** appears alongside the existing panels, collapsed by default
- [ ] Expanding reveals **Issue & Offer** and **Issue & Grant** buttons (disabled when not connected to KERIA)
- [ ] Clicking either opens `OfferOrGrantCredentialIssuanceDialog` in ad-hoc mode (no prior notification)
- [ ] Step 1 shows: Issuer picker (local AIDs only), Holder picker (local AIDs + connections with `(ext)` suffix), Role text field
- [ ] `AidNameValidator` rules render live below the role field (icon + text for each rule)
- [ ] "Next: Issue credential" is disabled until all rules pass (issuer selected, holder selected, issuer ≠ holder, valid role)
- [ ] Full flow: select issuer → select holder → enter role → Next → issuance spinner → credential review → confirm → success snackbar

### IPEX Apply (Presentation)
```js
window.postMessage({type: '/KeriAuth/ipex/apply', requestId: crypto.randomUUID(), payload: {schemaSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', recipient: 'EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3', isPresentation: true}}, window.location.origin);
```

### Developer Test: Request Credential Presentation (Apply)
1. Prerequisites: 
   a. Authenticated with at least one credential held
   b. Side Panel open
2. Navigate to Developer > Test
3. Click "Request Credential Presentation (Apply)"

Expected:
- [ ] RequestApproveIpexPage opens with title "Request to Present a Credential"
- [ ] Matching credentials are shown in a dropdown (first pre-selected)
- [ ] View selector shows (Tree-Full default)
- [ ] Credential detail renders with disclosure checkboxes on oneOf sections
- [ ] Switching between Card/Tree and Summary/Full updates the display
- [ ] Checking/unchecking disclosure boxes logs elision map changes
- [ ] Approve button is enabled when a credential is selected
- [ ] Selected credential has primary-color left border

### Grant/Offer Presentation from Notification
1. Prerequisites: Authenticated, with an `/exn/ipex/apply` notification
2. Navigate to Notifications, expand an apply notification

Expected:
- [ ] "Grant Presentation" and "Offer Presentation" buttons are active
- [ ] Clicking either opens the OfferOrGrantPresentationDialog
- [ ] Dialog shows credential selector with view selector
- [ ] Disclosure checkboxes appear on oneOf sections
- [ ] Full disclosure grant/offer: succeeds with "Credential presentation granted/offered" snackbar
- [ ] Selective disclosure grant: sends elided ACDC to signify-ts (may fail if anc/iss validation rejects it)
- [ ] Cancel closes dialog without action

### IPEX Agree (Issuance)
```js
window.postMessage({type: '/KeriAuth/ipex/agree', requestId: crypto.randomUUID(), payload: {offerSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', recipient: 'EFkSnI87zTv7LPOPZdXjoV52wCChfpUqYt7oGp7CjriJ', isPresentation: false}}, window.location.origin);
```

### IPEX Agree (Presentation)
```js
window.postMessage({type: '/KeriAuth/ipex/agree', requestId: crypto.randomUUID(), payload: {offerSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', recipient: 'EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3', isPresentation: true}}, window.location.origin);
```

### IPEX Grant (Issuance)
```js
window.postMessage({type: '/KeriAuth/ipex/grant', requestId: crypto.randomUUID(), payload: {acdc: {d: '', i: '', s: 'EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw', a: {d: '', i: '', dt: '2025-01-01T00:00:00Z', LEI: '5493001KJTIIGC8Y1R17', personLegalName: 'John Smith', engagementContextRole: 'Head of Standards'}}, recipient: 'EFkSnI87zTv7LPOPZdXjoV52wCChfpUqYt7oGp7CjriJ', isPresentation: false}}, window.location.origin);
```

### IPEX Grant (Presentation)
```js
window.postMessage({type: '/KeriAuth/ipex/grant', requestId: crypto.randomUUID(), payload: {said: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', recipient: 'EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3', isPresentation: true}}, window.location.origin);
```

### IPEX Admit (Issuance)
```js
window.postMessage({type: '/KeriAuth/ipex/admit', requestId: crypto.randomUUID(), payload: {grantSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', recipient: 'EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3', isPresentation: false}}, window.location.origin);
```

### IPEX Admit (Presentation)
```js
window.postMessage({type: '/KeriAuth/ipex/admit', requestId: crypto.randomUUID(), payload: {grantSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', recipient: 'EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3', isPresentation: true}}, window.location.origin);
```

### Same-Agent Notification Limitation

**Known limitation**: When sender and recipient are both local AIDs under the **same KERIA agent** (same-agent testing, e.g., CreateTestDataPage workflows), KERIA only generates notifications for *initiator* IPEX messages — `apply` and unsolicited `grant` (grant without a prior `agree` reference). Non-initiator messages (`offer`, `agree`, `admit`, and grant with `agreeSaid` properly set) do not generate notifications in this scenario.

**Root cause**: KERIA processes each outgoing exn twice — once on the sender-side via `agent.parser.parseOne` (which logs the exchange and sets `erpy[prior_said]`), and once on the recipient-side delivery (which re-verifies the exchange). For same-agent sends, both paths share the same database. The recipient-side `IpexHandler.verify()` finds `erpy` already set from the sender-side processing and returns `False`, suppressing the notification.

**Impact**: Same-agent automated workflows (CreateTestDataPage) will show only `apply` notifications on NotificationsPage. The actual IPEX exchange processing works correctly — credentials are issued and admitted regardless of notification visibility. Cross-agent workflows (two separate KERIA instances) generate all notifications as expected.


# Session Expiration Tests

## A. Inactivity Timeout Without User Activity
1. Prerequisite: Onboard and unlock the app
2. Set Preferences → Inactivity Timeout to 1 minute
3. Navigate to Dashboard and wait without any interaction

    Expected after ~30 seconds:
	- [ ] Countdown timer appears in AppBar (e.g., "Timeout in 29")

    Expected after ~60 seconds:
	- [ ] App navigates to Unlock page
	- [ ] Lock icon appears in AppBar
	- [ ] Session storage is cleared

## B. Inactivity Timeout With User Activity
1. Prerequisite: Onboard and unlock the app
2. Set Preferences → Inactivity Timeout to 1 minute
3. Navigate to Dashboard
4. Every ~20 seconds, click or press a key somewhere in the app

    Expected:
	- [ ] Countdown timer never appears (activity resets the 60-second timer)
	- [ ] Session remains unlocked indefinitely while activity continues

5. Stop interacting and wait 60+ seconds

    Expected:
	- [ ] Countdown timer appears after ~30 seconds of inactivity
	- [ ] App navigates to Unlock page after ~60 seconds of inactivity

## C. Session Persistence Across App Restart (Before Timeout)
1. Prerequisite: Onboard, unlock, set timeout to 1 minute
2. Close the extension popup/tab
3. Wait 30 seconds
4. Reopen the extension

    Expected:
	- [ ] App remains unlocked (not redirected to Unlock page)
	- [ ] Countdown timer visible in AppBar (time remaining from original session)

## D. Session Expiration During App Closure
1. Prerequisite: Onboard, unlock, set timeout to 1 minute
2. Close the extension popup/tab
3. Wait 70 seconds (past timeout)
4. Reopen the extension

    Expected:
	- [ ] App shows Unlock page (BackgroundWorker alarm cleared session)
	- [ ] Lock icon visible in AppBar
	- [ ] Service worker logs show alarm-triggered session clear

## E. Session Expiration after Browser Close, Restart
1. Prerequisite: Onboard, unlock, set timeout to 30 seconds
1. Navigate to Dashboard
1. Close entire browser (all windows)
1. Wait 45 seconds
1. Restart browser, open extension

	Expected:
	- [ ] App shows Unlock page (BackgroundWorker alarm cleared session)
	- [ ] Lock icon visible in AppBar
	- [ ] Service worker logs show alarm-triggered session clear

## F. Start App, Exit, Wait, Restart App
1. Prerequisite: onboard, unlock, set preferences to 1 minute
1. Start app
1. Exit app
1. Wait 30 seconds:
1. Start App

    Expected: 
	- [ ] not asked to Unlock
	- [ ] Countdown timer displayed in AppBar

1. Exit App
1. Wait 61 seconds
1. Restart App

 	Expected:
	- [ ] Unlock Page, since the BackgroundWorker�s expiration timer took effect.
	- [ ] Inspect the service-worker log to see this.  
	- [ ] App should have also detected any expired SessionExpiration in session.  (Could create manual test to tweak this in that storage.)

## G. Click or Keyboard Activity Resets Expiration Timer
1. Prerequisite: onboard, unlock, set preferences to 1 minute
1. Start app
1. Wait 15 seconds, click somewhere in the app
1. Wait another 20 seconds

	Expected: 
	- [ ] No countdown timer appears on the App Bar because the timer was reset.  

1. Wait another 30 seconds

	Expected: 
	- [ ] Countdown timer appears on the App Bar.  

1. Click somewhere in the app

	Expected: 
	- [ ] Expiration timer is reset and countdown no longer appears on AppBar

1. Wait 61 seconds

	Expected: 
	- [ ] Countdown timer appears after 30 seconds
	- [ ] After 60 seconds, app navigates to the Unlock page

# Notification Polling Tests

These tests verify the burst polling and recurring alarm mechanisms for KERIA notification fetching. The service worker must be able to become inactive between polls.

**Important**: For tests that check service worker inactivity (B, C, G), all DevTools panels must be closed — not just the SW DevTools, but also DevTools on any App page (e.g. extension tab, popup), since service worker log messages are surfaced there too and the connection prevents the SW from going inactive. Use `chrome://serviceworker-internals` to check SW status instead.

## A. Burst Polling on Connect
1. Prerequisite: Onboard and configure KERIA connection
2. Open DevTools on the service worker, enable "Preserve log"
3. Unlock and connect to KERIA

    Expected:
	- [ ] Service worker logs show "Starting burst polling (interval=5s, duration=120s)"
	- [ ] Logs show periodic `PollOnDemandAsync` calls every ~5 seconds
	- [ ] After ~120 seconds, logs show "Burst polling stopped"

## B. Service Worker Goes Inactive After Burst
**Note**: Any open DevTools panel (SW DevTools or App page DevTools) prevents the service worker from going inactive. Close all DevTools before this test and use `chrome://serviceworker-internals` to check status instead.

1. Prerequisite: Complete test A (burst polling finished)
2. Close all DevTools panels (SW and App pages)
4. Wait ~30 seconds after burst stops (no user interaction)
5. Open `chrome://serviceworker-internals` and find the extension's service worker

    Expected:
	- [ ] Service worker status shows "stopped" (inactive)

## C. Recurring Alarm Wakes Service Worker
**Note**: Close all DevTools panels before waiting, otherwise the SW stays alive and the test is inconclusive. Re-open DevTools after the alarm fires to check logs from the new SW lifetime.

1. Prerequisite: Connected to KERIA, burst polling completed, SW went inactive (test B)
2. Wait 5+ minutes (with SW DevTools closed)
3. Re-open DevTools on the service worker and check logs

    Expected:
	- [ ] Log shows `OnAlarmAsync: 'NotificationPollAlarm' fired`
	- [ ] A single `PollOnDemandAsync` executes
	- [ ] Service worker goes inactive again after the poll (close DevTools and verify via `chrome://serviceworker-internals`)

4. Verify alarm exists: in SW DevTools console, run `chrome.alarms.getAll(a => console.log(a))`

    Expected:
	- [ ] Array contains an alarm with name "NotificationPollAlarm" and periodInMinutes = 5

## D. User Activity Restarts Burst
1. Prerequisite: Connected to KERIA, burst polling has stopped
2. Click or press a key in the extension popup/tab
3. Check service worker logs

    Expected:
	- [ ] Logs show "Starting burst polling" again (new burst started)
	- [ ] Burst runs for ~120 seconds then stops

## E. NotificationsPage Opens Triggers Burst (If Not Active)
1. Prerequisite: Connected to KERIA, no active burst (wait for previous burst to finish)
2. Navigate to the Notifications page in the extension

    Expected:
	- [ ] Service worker logs show "Starting burst polling" (new burst started)

3. Navigate away and back to the Notifications page while burst is still active

    Expected:
	- [ ] No new "Starting burst polling" log (existing burst continues)

## F. Session Lock Clears Alarm
1. Prerequisite: Connected to KERIA, alarm exists
2. Lock the session (wait for inactivity timeout or manually lock)
3. In SW DevTools console, run `chrome.alarms.getAll(a => console.log(a))`

    Expected:
	- [ ] Logs show "Canceling notification polling and clearing alarm"
	- [ ] No "NotificationPollAlarm" in the alarms list
	- [ ] No further poll attempts after lock

## G. Alarm Survives Service Worker Restart
1. Prerequisite: Connected to KERIA, alarm exists
2. Close all DevTools panels
3. Force stop the service worker via chrome://serviceworker-internals
4. Wait for the alarm to fire (~5 minutes, or check the next alarm time)

    Expected:
	- [ ] Service worker wakes up when alarm fires
	- [ ] Logs show `EnsureInitializedAsync` running (reconnecting)
	- [ ] Logs show `OnAlarmAsync: 'NotificationPollAlarm' fired`
	- [ ] Poll executes successfully

## H. Connection Invite Triggers Burst
1. Prerequisite: Connected to KERIA
2. From a polaris-web page, initiate a connection invite
3. Approve the connection in the extension popup

    Expected:
	- [ ] Service worker logs show "Starting burst polling" after connection approval

# Seed KERIA with test data
1. Run typescript provided via Jupyter training notebook or script
1. Note the passcode(s) generated
1. ...

# Connect with KERIA with existing passcode
1. Prerequisite: Seed KERIA with test data
1. On Welcome page, select "Existing Passcode"
1. ...

# Connect with KERIA with new passcode
1. Prerequisite: Boot URL provided for KERIA
1. On Configure page, select "New Passcode"
1. ...

# Tab-Extension Permission and Content Script Tests

These tests verify the permission grant flow, content script injection, and icon state management.
Tests are ordered by causality - earlier test failures help identify root causes for later failures.

## A. Content Script Ping Response
**Purpose**: Verify the fundamental content script communication mechanism works.

1. Prerequisite: Have a tab with an active content script (permission previously granted, page loaded)
2. Open DevTools on the service worker
3. In console, run: `chrome.tabs.query({active: true, currentWindow: true}, tabs => chrome.tabs.sendMessage(tabs[0].id, {type: 'ping'}, r => console.log(r)))`

    Expected:
    - [ ] Response is `{ok: true}`

## B. Content Script Ready Message
**Purpose**: Verify content script announces itself on initialization.

1. Open DevTools on the service worker, enable "Preserve log"
2. Grant permission to a new site and reload the page
3. Check service worker console

    Expected:
    - [ ] Log shows "Content script ready in tab X"
    - [ ] Icon changes to active (colored) state

## C. Fresh Site Permission Grant (Action Button Click)
**Purpose**: Verify the complete permission grant flow for a new site.

1. Navigate to a site without prior permission (e.g., `http://localhost:8080`)
2. Click the extension action button
3. Accept the permission prompt

    Expected:
    - [ ] Permission prompt appears
    - [ ] After acceptance, one-shot content script injected
    - [ ] Persistent content script registered
    - [ ] Reload prompt appears
    - [ ] After reload, icon shows active (colored) state
    - [ ] Page can communicate with extension

## D. Pre-Granted Permission (Action Button Click)
**Purpose**: Verify flow when permission was previously granted but script not registered.

1. Prerequisite: Site has permission but content script was unregistered
2. Click the extension action button

    Expected:
    - [ ] No permission prompt (already granted)
    - [ ] Content script registered
    - [ ] Reload prompt appears
    - [ ] After reload, icon shows active state

## E. Already Active Site (Action Button Click)
**Purpose**: Verify no action when content script is already active.

1. Prerequisite: Site has active content script (icon is colored)
2. Click the extension action button

    Expected:
    - [ ] No prompts appear
    - [ ] Icon remains active (colored)
    - [ ] Service worker log shows "Content script is active and responding"

## F. Stale Content Script Detection (Action Button Click)
**Purpose**: Verify detection of orphaned content script after extension reload.

1. Prerequisite: Site has active content script
2. Reload extension via chrome://extensions
3. Click the extension action button on the same tab

    Expected:
    - [ ] Reload prompt appears ("needs to refresh the connection")
    - [ ] Icon shows inactive (grey) state until reload
    - [ ] After page reload, icon shows active state

## G. Icon State on Tab Activation
**Purpose**: Verify icon updates when switching between tabs.

1. Open two tabs: one with permission (Tab A), one without (Tab B)
2. Ensure Tab A has active content script (reload if needed)
3. Switch between tabs

    Expected:
    - [ ] When Tab A is active: icon shows active (colored) state
    - [ ] When Tab B is active: icon shows inactive (grey) state

## H. Icon State on Page Navigation
**Purpose**: Verify icon updates when page finishes loading.

1. Have a tab with permission granted
2. Navigate to the permitted site
3. Wait for page load complete

    Expected:
    - [ ] Icon shows active (colored) state after page load
    - [ ] Service worker log shows ping succeeded

## I. Permission Removal via Context Menu
**Purpose**: Verify cleanup when permission is removed.

1. Prerequisite: Site has active content script
2. Right-click extension icon → remove permission for site
3. Accept reload prompt (if shown)

    Expected:
    - [ ] Content script unregistered
    - [ ] Icon shows inactive (grey) state
    - [ ] Reload prompt appears
    - [ ] After reload, content script no longer present

## J. Extension Reload Recovery
**Purpose**: Verify state recovery after extension reload.

1. Prerequisite: Site has active content script
2. Go to chrome://extensions
3. Click reload button on the extension
4. Return to the tab with the site

    Expected:
    - [ ] Icon shows inactive (grey) state (content script context invalidated)
    - [ ] Clicking action button offers reload prompt
    - [ ] After page reload, icon shows active state
    - [ ] Content script functions correctly

## K. Browser Restart Recovery
**Purpose**: Verify state recovery after browser restart with restored tabs.

1. Prerequisite: Site has active content script
2. Close entire browser (with session restore enabled)
3. Reopen browser (tabs restore)

    Expected:
    - [ ] Tab is restored
    - [ ] Content script re-injects automatically (registered script persists)
    - [ ] Icon shows active (colored) state
    - [ ] `cs-ready` message received by service worker
    - [ ] Page can communicate with extension without manual action

## L. Browser Resume from Suspend
**Purpose**: Verify state recovery after machine suspend/resume.

1. Prerequisite: Site has active content script
2. Suspend machine (sleep/hibernate)
3. Resume machine

    Expected:
    - [ ] Icon shows correct state for active tab
    - [ ] Content script remains functional
    - [ ] No user action required

## M. New Tab with Permitted URL
**Purpose**: Verify new tabs to permitted sites get content script.

1. Prerequisite: Permission granted for a site
2. Open a new tab and navigate to the permitted site

    Expected:
    - [ ] Content script injected automatically (registered script)
    - [ ] Icon shows active (colored) state after page load
    - [ ] `cs-ready` message received by service worker

## N. Unsupported URL Schemes
**Purpose**: Verify graceful handling of chrome://, about:, etc.

1. Navigate to `chrome://extensions` or `about:blank`
2. Observe icon state

    Expected:
    - [ ] Icon shows inactive (grey) state
    - [ ] No errors in service worker console
    - [ ] Clicking action button does nothing (no permission prompt)

## O. Service Worker Startup Initialization
**Purpose**: Verify icon state is correct immediately after service worker starts.

1. Open DevTools on service worker, enable "Preserve log"
2. Navigate to a site with active content script
3. Force service worker restart: chrome://serviceworker-internals → Stop → Start
4. Check icon state immediately

    Expected:
    - [ ] Log shows "Initializing state for active tab"
    - [ ] Icon shows correct state (active if CS responding, inactive otherwise)

## P. Service Worker Cold-Start Wake via Tab Switch
**Purpose**: Verify module-level listeners catch events that wake the service worker from idle.

1. Prerequisite: Two tabs open — Tab A with active content script, Tab B without
2. Wait for service worker to go idle (~30s inactivity, or force stop via chrome://serviceworker-internals)
3. Switch from Tab B to Tab A (this wakes the service worker via chrome.tabs.onActivated)

    Expected:
    - [ ] Service worker wakes up
    - [ ] Icon updates to active (colored) state for Tab A
    - [ ] No missed events, no stale icon state

## Q. Service Worker Cold-Start Wake via Permission Grant
**Purpose**: Verify permission listeners catch events that wake the service worker from idle.

1. Wait for service worker to go idle (~30s, or force stop)
2. Right-click extension icon on a new site → grant permission via context menu

    Expected:
    - [ ] Service worker wakes up
    - [ ] Content script registered for the site
    - [ ] Reload prompt appears
    - [ ] After reload, icon shows active state

# Webauthn Authenticator ("Passkey") Tests

## A. Register and Test Webauthn Authenticator

## B. Authenticate with Webauthn Authenticator

## C. Fail Webauthn Authentication

## D. Remove Webauthn Authenticator

## E. Fallback to Passcode Authentication

## F. Multiple Webauthn Authenticators

## G. Webauthn Authenticator on Different Browsers/Devices

# Network Resilience Tests

These tests verify the extension's behavior when network connectivity is unstable or KERIA is unreachable.

**Logging**: Set these namespaces to `"Debug"` in `Extension/wwwroot/appsettings.json` before testing:
- `Extension.BackgroundWorker`
- `Extension.Services.AppCache`
- `Extension.Services.NotificationPollingService`
- `Extension.Services.SignifyService.SignifyClientService`
- `Extension.Services.NetworkConnectivityService`

## A. Browser Offline Detection
1. Prerequisite: Onboard, unlock, connected to KERIA
2. Open Developer > State page
3. Toggle network off in Chrome DevTools (Network tab > Offline checkbox)

    Expected:
    - [ ] AppBar shows WifiOff icon (warning color) with tooltip "No internet connection"
    - [ ] Developer State page shows `IsNetworkOnline = false`
    - [ ] Service worker logs: `NetworkConnectivityService: Network state changed — IsOnline=False`
    - [ ] Service worker logs: `NetworkState written: IsOnline=False`
    - [ ] Notification polling logs: `PollOnDemandAsync: Skipped — browser is offline`

4. Toggle network back on

    Expected:
    - [ ] AppBar returns to green link icon (or CloudOff if KERIA still unreachable)
    - [ ] Developer State page shows `IsNetworkOnline = true`
    - [ ] Service worker logs: `NetworkState written: IsOnline=True`

## B. KERIA Server Unreachable
1. Prerequisite: Onboard, unlock, connected to KERIA
2. Stop the KERIA server (or block its port)
3. Trigger a KERIA operation (e.g., navigate to a page that fetches credentials, or wait for notification poll)

    Expected:
    - [ ] AppBar shows CloudOff icon (warning color) with tooltip "Cannot reach KERIA server — retrying..."
    - [ ] Developer State page shows `IsKeriaReachable = false`
    - [ ] Service worker logs show `network_error` classification and retry attempts for read-only operations
    - [ ] Console shows retry attempts: signifyClient errors followed by delays (1s, 2s)

4. Restart the KERIA server
5. Wait for the next notification poll or trigger a manual operation

    Expected:
    - [ ] AppBar returns to green link icon
    - [ ] Developer State page shows `IsKeriaReachable = true`

## C. Retry on Transient Network Failure
1. Prerequisite: Connected to KERIA
2. In Chrome DevTools Network tab, enable throttling or briefly toggle offline during a credential fetch
3. Watch the service worker console

    Expected:
    - [ ] Read-only operations (notificationsList, getCredentialsList, etc.) retry up to 2 times with exponential backoff (1s, 2s)
    - [ ] Console shows: `signifyClient: <operationName> error` followed by retry attempts
    - [ ] If the network recovers during retries, the operation succeeds without user intervention
    - [ ] Non-idempotent operations (createAID, ipexAdmit, etc.) do NOT retry — they fail immediately

## D. Service Worker Dormancy and Network State Recovery
1. Prerequisite: Connected to KERIA
2. Close all DevTools panels
3. Wait for service worker to go inactive (~30s)
4. Toggle network off, wait 5 seconds, toggle back on
5. Wait for the next alarm-triggered poll (or manually wake the SW)

    Expected:
    - [ ] On wake, service worker re-checks `navigator.onLine` and writes correct `NetworkState`
    - [ ] AppBar shows correct connectivity state after SW wake

## E. Developer State Page Accuracy
1. Prerequisite: Onboard and unlock
2. Navigate to Developer > State

    Expected:
    - [ ] `IsNetworkOnline` shows at top of state tree with label "Browser reports network available"
    - [ ] `IsKeriaReachable` shows with label "KERIA endpoint reachable"
    - [ ] Both update reactively when connectivity changes (no page refresh needed)

# Credential Presentation

## A. Saidify Verification Harness (Developer Test Page)
1. Prerequisite: Onboard, unlock, and have at least one issued credential in cache (ideally ECR vLEI with chained credentials)
2. Navigate to Developer > Test, scroll to **Saider operations**
3. Pick a credential from the dropdown (labeled by `sad.d`), click **Verify saidify on sections**

    Expected:
    - [ ] A "Section saidify" table appears with one row per `sad.<key>` for the root credential and every chained credential at any depth
    - [ ] Path column shows `a`, `e`, `r` for the root and `chains[0].a`, `chains[0].chains[1].e`, etc. for nested; indentation reflects chain depth
    - [ ] Rows with a populated stored `d` show Status `PASS` (computed SAID equals stored `d`)
    - [ ] Rows with empty stored `d` (e.g. `e`, `r`) show Status `N/A (no stored SAID)` and a non-empty Computed SAID for manual inspection
    - [ ] An "Edge references" table lists every descendant `n` field; each shows `MATCHED → <chain path>` if the referenced SAID matches some chained credential, else `UNMATCHED`
    - [ ] A "Chained credentials back-check" table lists every non-root chained credential with `USED` or `ORPHAN`
    - [ ] Snackbar summary reports `Saidify: X PASS, Y FAIL, Z N/A — refs: U UNMATCHED, chains: O ORPHAN`; FAIL/UNMATCHED/ORPHAN counts should all be zero on a well-formed credential

## B. Credential Presentation Page — Undisclosed SAID Preview
1. Prerequisite: A credential whose `sad.e.d` or `sad.r.d` is empty in the raw ACDC (most issuer-output vLEI credentials)
2. Navigate to `chrome-extension://<id>/Credential.html?said=<SAID>&isPresentation=true`

    Expected:
    - [ ] A brief `MudProgressLinear` appears at the top of the credential panel while SAIDs are computed
    - [ ] Once filled, the tree shows disclosure checkboxes for `Attributes`, `Edges`, `Rules` (and any nested `oneOf` sections)
    - [ ] With a section **undisclosed**, a monospace SAID preview row appears at indent N+1 below the checkbox row — **including for Edges and Rules** (these blocks had empty `d` in the raw credential; the displayed SAID is computed via signify-ts' `Saider.saidify`)
    - [ ] Toggling the checkbox to disclosed collapses the preview and expands the children; toggling back restores the preview
    - [ ] Each disclosure tag reads `[undisclosed]`, `[disclosed]`, or `[partially disclosed]` consistent with whether nested oneOf/chained descendants are all disclosed

## C. Credential Presentation Selector — Fill on Switch
1. Prerequisite: Multiple credentials matching the same schema, with at least one having empty `sad.e.d`
2. Trigger an IPEX Apply (presentation) flow that surfaces `CredentialPresentationSelector`
3. Switch between credentials in the selector dropdown

    Expected:
    - [ ] Progress bar briefly appears on each new selection while saidification runs
    - [ ] Previously-filled credentials re-render without a progress bar on return (fill is keyed by `sad.d`)
    - [ ] Disclosure tree shows SAID previews on undisclosed Edges/Rules for the currently-selected credential

## D. Unified Tree Rendering (Presentation and Non-Presentation Parity)
1. Prerequisite: a credential with at least one chained credential (e.g., ECR vLEI or LE vLEI)
2. Navigate to `chrome-extension://<id>/Credential.html?said=<SAID>` (no `isPresentation` query param; non-presentation mode)

    Expected:
    - [ ] Root credential's sections (`a`, `e`, `r`) render as indented tree rows using the same `bt-credview-row` styling as in presentation mode
    - [ ] Each chained credential shows a **single header row** at the left margin (same indent as root's section headers) labeled with the chained credential's `ShortName` or `SchemaTitle` — NO `MudExpansionPanel` / collapsible affordance
    - [ ] The chained credential's own sections are indented one level deeper than its header row (e.g., at `padding-left: 1.25rem`)
    - [ ] Attribute rows inside the chained credential are indented deeper still
    - [ ] Two-level nesting (chain-within-chain) shows three distinct indent levels
3. Navigate to the same credential with `?isPresentation=true`

    Expected:
    - [ ] Structural layout (indentation, row placement) is **identical** to the non-presentation view
    - [ ] The only added visual elements are the disclosure checkboxes (in the gutter column) and the `[disclosed]`/`[undisclosed]`/`[partially disclosed]` tags on elision-toggleable rows
    - [ ] SAID-preview rows on undisclosed sections still appear at `Depth+1` below the checkbox row (behavior from "B. Credential Presentation Page")

## E. Inline Chained Credential Under Matching SaidReference (Display Only)
1. Prerequisite: a credential with at least one edge whose `n` field references a chained credential's SAID (e.g., ECR vLEI whose `sad.e.<edge>.n` matches the bundled chain's `sad.d`)
2. Navigate to `chrome-extension://<id>/Credential.html?said=<SAID>` (non-presentation)

    Expected:
    - [ ] The SaidReference node (edge's `n`) renders with the chained credential's full tree indented below it (one indent level deeper than the SaidReference row)
    - [ ] The chained credential does **NOT** additionally appear as a top-level sibling of the root's sections — it's consumed into the inline position
    - [ ] If an edge's `n` does not match any chained credential (orphan), the existing SAID digest row renders instead (no crash, no inline)
3. Navigate with `?isPresentation=true` and toggle the disclosure checkbox on the SaidReference row

    Expected:
    - [ ] When **undisclosed** (checkbox off): the SAID digest row shows (abbreviated), chained credential hidden
    - [ ] When **disclosed** (checkbox on): the chained credential's full tree renders inline in place of the digest row, with its own elision checkboxes usable independently
    - [ ] Either way, the chained credential is not duplicated at top level
4. Confirm the data model integrity

    Expected:
    - [ ] On the Developer Test Page Saidify Verification Harness (section A), the "Edge references" table still shows `MATCHED → chains[i]` and the "Back-check" table still shows `USED` for the referenced chain — the display restructuring is purely visual; the underlying credential remains unchanged

## F. Abbreviate SAIDs Preference
1. Prerequisite: Credential with visible SAIDs in the tree view (any ECR vLEI, ideally with chained credentials so multiple SAIDs appear)
2. Navigate to `chrome-extension://<id>/Credential.html?said=<SAID>`
3. Click the gear icon (top-right of CredentialViewPrefsComponent); verify a new **"Abbreviate SAIDs"** checkbox appears, defaulted to **on**
4. Observe the tree view

    Expected (abbreviate on):
    - [ ] All SAID displays (undisclosed preview rows, SaidReference digest rows) render truncated with an ellipsis (e.g., `EGxR_dXx…WhjK`), with the full SAID available via browser tooltip on hover
    - [ ] The Developer Test Page Saidify Verification Harness tables continue to render SAIDs in abbreviated form (regardless of user preference — they are forced-abbreviated for table compactness)

5. Uncheck "Abbreviate SAIDs" in the prefs

    Expected (abbreviate off):
    - [ ] SAIDs in the credential tree render in full (no ellipsis)
    - [ ] The change is immediate (no page reload required) and persists across extension reloads
    - [ ] Developer Test Page tables remain abbreviated (override in effect)

6. Re-check the preference

    Expected:
    - [ ] Reverts cleanly to abbreviated form


