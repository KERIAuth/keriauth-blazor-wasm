# KERI Auth Manual Test Case Outline

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
### KERI Auth Install Source / Variant
- [ ] Developer Load via Extension page
	- [ ] Debug
	- [ ] Release
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
The following can be pasted into the DevTools console on a page with injected Content Script:

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
```js
window.postMessage({type: '/signify/sign-data', requestId: crypto.randomUUID(), payload: {message: 'Please sign this data', items: ['item-to-sign-1', 'item-to-sign-2']}}, window.location.origin);
```

## Create Data Attestation Credential
```js
window.postMessage({type: '/signify/credential/create/data-attestation', requestId: crypto.randomUUID(), payload: {schemaSaid: 'EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao', credData: {d: '', u: '', i: '', dt: '2025-01-01T00:00:00Z', LEI: '5493001KJTIIGC8Y1R17'}}}, window.location.origin);
```

## Connection Invite
```js
window.postMessage({type: '/KeriAuth/connection/invite', requestId: crypto.randomUUID(), payload: {oobi: 'https://keria-ext.dev.idw-sandboxes.cf-deployments.org/oobi/EFMPf5HdMA3Wd09_Rq3hNjgRFw1XKhHeuIW6Noqhszd3/agent/EMhtGVe_k0b0NQXqJvJzm5NvhYkggbnLkKNaTtxmOcxe?name=CF%20Credential%20Issuance4'}}, window.location.origin);
```

## Unknown / Unimplemented Request
```js
window.postMessage({type: '/signify/some-future-method', requestId: crypto.randomUUID(), payload: {foo: 'bar'}}, window.location.origin);
```

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

# Webauthn Authenticator ("Passkey") Tests

## A. Register and Test Webauthn Authenticator

## B. Authenticate with Webauthn Authenticator

## C. Fail Webauthn Authentication

## D. Remove Webauthn Authenticator

## E. Fallback to Passcode Authentication

## F. Multiple Webauthn Authenticators

## G. Webauthn Authenticator on Different Browsers/Devices

# See also
ManualTest.md in this folder for detailed step-by-step happy-path test cases.






