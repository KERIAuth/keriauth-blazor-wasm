# KERI Auth Product Install and Happy Path Testing


## A. Download a test report
1. Download a test report, which you will later upload and submit
    1. From a browser tab, download the file [Banca_Marentina_report_to_EBA_2024-12-31.zip](./Reports/Banca_Marentina_report_to_EBA_2024-12-31.zip), and store it locally.

## B. Product Install and Configuration

1. Install KERI Auth extension
    1. Open a Chrome browser
    1. Install the KERI Auth Extension, following one of these options:
        1. Navigate in the Chrome Web Store to the current release, here: https://chromewebstore.google.com/detail/keri-auth/jidldhmnhdelhfcfmlhdojkfgcbalhhj and click on Add to Chrome.  Acknowledge the warning on “Add KERI Auth?” and press “Add extension”
        1. Or, if you want an unsigned pre-release, use the build-artifacts link here: https://github.com/KERIAuth/keriauth-blazor-wasm/actions/runs/12354002582    and unzip that download and the KeriAuth.zip file within it. In Chrome, you'll need to navigate to Manage Extensions, enable Developer Mode, then press Load Unpacked.

1. Navigate through the onboarding pages in KERI Auth
    <!-- Updated Release -->
    <img src="images/image5.png" width="500" />

    <!-- Terms -->
    <img src="images/image9.png" width="500" />

    <!-- Accept Terms -->
    <img src="images/image10.png" width="500" />

1. On the Configure Screen
    1. Select the Preset “Roots ID”
    1. Clear the Boot URL string
    1. Copy the “Demo signify passcode” found on this page: https://github.com/GLEIF-IT/reg-pilot?tab=readme-ov-file#demo-test-instances
    1. Paste the passcode on the clipboard into the passcode field
    1. Press Connect

    <!-- Configure -->
    <img src="images/image7.png" width="500" />

1. You’ll now see this Dashboard screen:

    <!-- Home -->
    <img src="images/image1.png" width="500" />

1. You may now explore features via the menu.


## C. Pin the KERI Auth extension action icon

1. From the browser extension icon area, pin the KERI Auth action button

    <!-- action icon pin -->
    <img src="images/image4.png" width="500" />

## D. Navigate to and test the Reg-Pilot-Webapp

1. From the current browser tab or a new one, navigate to https://reg-pilot-webapp-dev.rootsid.cloud/

1. Click on Select Credential button

1. If needed, click on the KERI Auth action icon and refresh the page just opened

1. When seeing the popup, if it is locked, you may need to: 

    1. Enter the same passcode as earlier.
    1. Press Unlock.

    <!-- Unlock in Popup -->
    <img src="images/image6.png" width="500" />

1. Select the Identifier called “role” and the second credential on the list:
    <!-- Request to Sign In -->
    <img src="images/image3.png" width="500" />

1. Press Sign In. 
    * If you see a spinning cursor, the website has timed out. You’ll need to refresh the page and repeat these steps above.
    * A successful interaction should result in the following:

    <!-- success after Sign In -->
    <img src="images/image8.png" width="500" />

1. Navigate to Reports

    <img src="images/image14.png" width="500" />

1. Press SELECT FILE or drag and drop the zip file you just downloaded.

    <img src="images/image15.png" width="500" />

1. Press SUBMIT REPORT

1. On the KERI Auth popup, click on Show Details, then press Sign Requests

    <!-- request to sign POST -->
    <img src="images/image2.png" width="500" />

    * Note, if you get an error from the page like [Object object], the website may have timed out, and you’ll need to refresh the tab and re-start the flow from https://reg-pilot-webapp-test.rootsid.cloud/ 
    * Since this site is not under the control of KERI Auth, it may restrict what can be uploaded.

1. A successful submission will be confirmed, similar to:

    <!-- Successfuly submitted -->
    <img src="images/image16.png" width="500" />
   




<!-- 
1. Prepare dependant components to use KERIA to use KERIA and https://reg-pilot-webapp-dev.rootsid.cloud/ hosted by RootsID.  Contact Ed Eykholt privately if you’d like this passcode.  
1. Install your own following: https://github.com/GLEIF-IT/reg-pilot/tree/main/signify-ts-test  which has docker-compose, test data generation, tests, etc. and here which has webapp based testing, test data, etc https://github.com/GLEIF-IT/reg-pilot-webapp/tree/main/my-app 
-->





