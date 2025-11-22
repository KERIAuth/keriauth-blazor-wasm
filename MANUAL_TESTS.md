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
1. In Chrome’s Extension page, hit Refresh (assuming a developer load)
- [ ] Expected: see Welcome page and New Version info

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

# Session Expiration Tests
## Start App and Wait
1. Prerequisite: onboard and navigate to Dashboard
1. Set preferences to timeout in 1 minute
1. Wait

    Expected: 
	- [ ] After 30 seconds, the countdown timer appears on the App Bar.  
	- [ ] After 60 seconds, the Chrome.Storage.Session is cleared.  
	- [ ] AppBar Lock icon appears. 
	- [ ] App navigates to the Unlock page.

## Start App, Exit, Wait, Restart App
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
	- [ ] Unlock Page, since the BackgroundWorker’s expiration timer took effect.
	- [ ] Inspect the “service-worker” log to see this.  
	- [ ] App should have also detected any expired SessionExpiration in session.  (Could create manual test to tweak this in that storage.)

## Click or Keyboard Activity Resets Expiration Timer
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

# Webauthn Authenticator Tests




