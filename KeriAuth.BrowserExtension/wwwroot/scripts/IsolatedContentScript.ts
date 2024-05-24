/// <reference path="./CommonInterfaces.d.ts" />

import { IMessage } from "./CommonInterfaces";

// This Isolated ContentScript is inserted into pages in response to a user clicking on the extension action button,
// And dependent on specified URL patterns in the manifest.json file.
// The purpose of the IsolatedContentScript is primarily to shuttle messages from a dApp's
// web pages to/from the extension service worker.
// A dApp page will include and use the MainContentScript.js and its methods
// to create and respond to these messages

// TODO P3 should use declarative events API, for performance reasons, since they will be evaluated in the browser, not javascript.
// See https://developer.chrome.com/docs/extensions/reference/events/

// Listen to messages from the web page
window.addEventListener(
    "message",
    (event: MessageEvent) => {
        let currentLocation = window.location;
        if (event.origin != currentLocation.origin) {
            // Check origin. See security concern https://developer.mozilla.org/en-US/docs/Web/API/Window/postMessage#security_concerns
            console.log("invalid origin")
            return;
        }
        let message: IMessage  = event.data
        if (message != null && message != undefined) {
            // console.log('IsolatedContentScript received:');
            // console.log(message)
            // We only accept messages from ourselves
            if (event.source != window) {
                return;
            }
            if (message.name == 'WALLET_REQUEST_MSG') {
                //sending a message to the extension
                chrome.runtime.sendMessage(message, function (response) {
                    // optional callback
                    console.log("sendMessage completed: ", message);
                    // send a msg back to the calling page
                    //    window.postMessage({
                    //        type: "FROM_INJECTED_CREDENTIAL_ISSUING_REQUEST",
                    //        text: "Please open your wallet and sign the credential :)",
                    //    });
                });
            } else {
                // The message is not for us
                // console.log("IsolatedContentScript: unexpected: ", event.data.type);
            }
        } else {
            console.log("IsolatedContentScript: unexpected event.data");
        }
    },
    false
);

// Listen to messages from the extension
// See https://developer.chrome.com/docs/extensions/mv3/content_scripts/#host-page-communication
chrome.runtime.onMessage.addListener(
    function (request: any, sender, sendResponse) {
        // send message back to the calling page
        // console.log("Received something back")
        if (request.messageType === 'WALLET_RESPONSE_MSG') {
            // console.log("now forwarding to HMTL")
            window.postMessage(request);
        }
        if (request.messageType === 'CLOSE_IFRAME_MSG') {
            // console.log("now forwarding to HMTL")
            // window.postMessage(request);
            window.postMessage(request);
        }
    });