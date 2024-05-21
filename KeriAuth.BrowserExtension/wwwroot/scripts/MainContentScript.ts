//// KeriAuth.BrowserExtension - IntegratedScript.js
//// This script is injected into web pages and runs in the Web Page Context.
//// It sends messages to the extension's IsolatedContentScript
///  via the window.postMessage API and it receives message via window message events.

//// TODO P3 Function names should be in a namespace (or a { } block to avoid creating global variables)? more unique, plus methods should include a version parameter for future-proofing of dApp page integrations

import { IMessage, IWalletRequest } from './CommonInterfaces.js';

let walletRequestCallback;
//let credentialProofRequestCallback;
let currentLocation = window.location;

//Placeholder to test the detection of the extension
const blocktrust = () => {
}

//Function to close the overlay and the contained iframe
(window as any).closeOverlay = () => {
    const iframeWrapper = document.getElementById("bt-overlay-iframeWrapper");
    const overlay = document.getElementById("bt-overlay");
    if (iframeWrapper) {
        iframeWrapper.remove();
    }
    if (overlay) {
        overlay.remove();
    }
}

const sendWalletRequest = (walletRequest: IWalletRequest, walletRequestReturnFunction: any) => {
    if (!walletRequest || !walletRequest.content || !walletRequest.type) {
        return;
    }
    const existingOverlay = document.getElementById("bt-overlay");
    if (!existingOverlay && walletRequest.iframeMode) {
        //Create overlay
        var overlay = document.createElement("div");
        overlay.setAttribute("id", "bt-overlay");
        overlay.style.position = "fixed";
        overlay.style.top = "0";
        overlay.style.left = "0";
        overlay.style.width = "100%";
        overlay.style.height = "100%";
        overlay.style.background = "rgba(0,0,0,0.5)";
        overlay.style.zIndex = "1000";
        document.body.appendChild(overlay);

        //Create iframe wrapper
        var iframeWrapper = document.createElement("div");
        iframeWrapper.setAttribute("id", "bt-overlay-iframeWrapper");
        iframeWrapper.style.width = "340px";
        iframeWrapper.style.height = "600px";
        iframeWrapper.style.border = "none";
        iframeWrapper.style.position = "fixed";
        iframeWrapper.style.background = "white";
        iframeWrapper.style.top = "50%";
        iframeWrapper.style.left = "50%";
        iframeWrapper.style.transform = "translate(-50%, -50%)";
        iframeWrapper.style.zIndex = "1001";
        document.body.appendChild(iframeWrapper);

        //Create iframe
        //We have to pass the location of the parent page to the iframe, so that the extension can message back to the parent page
        var ifrm = document.createElement("iframe");
        // TODO P1 .getURL may throw an exception because of lack of tabs permission in manifest.json (which should not be added back)
        var extensionUrl = chrome.runtime.getURL;
        // extensionUrl will be similar to "chrome-extension://encebebaejmobnlmldfbpmnfpeihnbfl/index.html"
        ifrm.setAttribute("src", extensionUrl + "?environment=iframe&location=" + currentLocation.href);
        ifrm.setAttribute("id", "bt-overlay-iframe");
        ifrm.style.width = "340px";
        ifrm.style.height = "600px";
        ifrm.style.border = "none";
        iframeWrapper.appendChild(ifrm);

        //Create close button and attach it to the wrapper
        var closeBtn = document.createElement("button");
        closeBtn.setAttribute("id", "bt-overlay-closebtn");
        closeBtn.style.position = "fixed";
        closeBtn.style.left = "350px";
        closeBtn.style.borderRadius = "50%";
        closeBtn.style.zIndex = "1002";
        closeBtn.style.background = "white";
        closeBtn.style.border = "none";
        closeBtn.style.outline = "none";
        closeBtn.style.cursor = "pointer";
        closeBtn.style.padding = "0";
        closeBtn.style.margin = "0";
        closeBtn.style.width = "30px";
        closeBtn.style.height = "30px";
        closeBtn.style.fontFamily = "Arial, Helvetica, sans-serif";
        closeBtn.style.fontSize = "20px";
        closeBtn.innerText = "X";
        closeBtn.addEventListener("click", (window as any).closeOverlay);
        iframeWrapper.appendChild(closeBtn);
    }

    walletRequestCallback = walletRequestReturnFunction;
    let content = JSON.stringify(walletRequest);
    // console.log('Sending msg: ' + content)
    window.postMessage(
        {
            name: "WALLET_REQUEST_MSG",
            walletRequest: walletRequest,
            sourceHostname: window.location.hostname,
            sourceOrigin: window.location.origin,
            contentHash: hashCode(content),
            timestamp: Date.now(),
            iframeMode: walletRequest.iframeMode,
        } as IMessage,
        currentLocation.origin //restriction to same origin targets only, previously used "*"
    );
}

function hashCode(str: string): string {
    return Array.from(str)
        .reduce((s: any, c: any) => Math.imul(31, s) + c.charCodeAt(0) | 0, 0).toString()
}


(() => {
    console.log("KERI Auth - IntegratedScript");

    // This event listener only listens to messages from the extension to close the overlay/iframe
    // It is here, that the developer doesnt have to implement the closing in their own code
    window.addEventListener(
        "message",
        (event) => {
            if (event.data && event.data.messageType === 'CLOSE_IFRAME_MSG') {
                (window as any).closeOverlay();
            }
        },
        false
    );
    return true;
})();