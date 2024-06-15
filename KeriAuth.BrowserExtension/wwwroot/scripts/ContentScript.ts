/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

import { IMessage, IBaseMsg, ICsSwMsg, IExCsMsg } from "./CommonInterfaces";


//interface IMessage {
//    name: string,
//    sourceHostname: string;
//    sourceOrigin: string;
//    windowId: number;
//}

//interface IBaseMsg {
//    name: string,
//}

//interface ICsSwMsg {
//    name: string,
//}

//interface IExCsMsg {
//    name: string,
//}



// signify-brower-extension compliant page message types
// Note this is called TAB_STATE and others in the signify-browser-extension
enum PAGE_EVENT_TYPE {
    SELECT_IDENTIFIER = "select-identifier",
    SELECT_CREDENTIAL = "select-credential",
    SELECT_ID_CRED = "select-aid-or-credential",
    SELECT_AUTO_SIGNIN = "select-auto-signin",
    NONE = "none",
    VENDOR_INFO = "vendor-info",
    FETCH_RESOURCE = "fetch-resource",
    AUTO_SIGNIN_SIG = "auto-signin-sig",
}
enum PAGE_POST_TYPE {
    SIGNIFY_EXT = "signify-extension",
    SELECT_AUTO_SIGNIN = "select-auto-signin",
    SIGNIFY_SIGNATURE1 = "signify-signature",
    SIGNIFY_SIGNATURE2 = "signify-signature",
}

// Define types for message events and chrome message
interface ChromeMessage {
    type: string;
    subtype?: string;
    [key: string]: any;
}

interface EventData {
    type: string;
    [key: string]: any;
}

// interfaces for the messages posted to the web page
interface BaseMessage {
    version: string;
    type: PAGE_POST_TYPE;
}

interface SignifyExtensionMessage extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SIGNIFY_EXT;
    data: {
        extensionId: string;
    };
}

interface SignifySignature1Message extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SIGNIFY_SIGNATURE1;
    data: any;
    requestId: string;
    rurl: string;
}

interface SignifySignature2Message extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SIGNIFY_SIGNATURE2;
    requestId: string;
    data: any;
}

interface SignifyAutoSigninMessage extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SELECT_AUTO_SIGNIN;
    requestId: string;
    rurl: string;
}

// Union type for all possible messages that can be sent to the web page
// type PageMessage = SignifyExtensionMessage | SignifySignature1Message | SignifySignature2Message | SignifyAutoSigninMessage;


// get the tabId of the page, to be used for unique identification of port
var myTabId: number = 0;
var mm: IMessage = {
    name: "getTabId",
    sourceHostname: "",
    sourceOrigin: "",
    windowId: 0,
};

chrome.runtime.sendMessage(mm, (response) => {
    console.log("KERI_Auth_CS to extension: tabId: ", response);
    // myTabId = tabId;
});

console.log("tabId: ", myTabId);

const port = chrome.runtime.connect({ name: String(myTabId) });


// Handle messages from web page
window.addEventListener(
    "message",
    async (event: MessageEvent<EventData>) => {
        // Accept messages only from same window
        if (event.source !== window) {
            return;
        }
        console.log("KERI_Auth_CS from page: ", event.data);

        switch (event.data.type) {
            case PAGE_EVENT_TYPE.SELECT_IDENTIFIER:
            case PAGE_EVENT_TYPE.SELECT_CREDENTIAL:
            case PAGE_EVENT_TYPE.SELECT_ID_CRED:
            case PAGE_EVENT_TYPE.SELECT_AUTO_SIGNIN:
            case PAGE_EVENT_TYPE.NONE:
            case PAGE_EVENT_TYPE.VENDOR_INFO:
            case PAGE_EVENT_TYPE.FETCH_RESOURCE:
            case PAGE_EVENT_TYPE.AUTO_SIGNIN_SIG:
            default:
                port.postMessage({ type: event.data.type, data: event.data });
                return;
        }
    }
);

// Handle messages from extension
chrome.runtime.onMessage.addListener(async function (
    message: ChromeMessage,
    sender: chrome.runtime.MessageSender,
    sendResponse: (response?: any) => void
) {
    if (sender.id === chrome.runtime.id) {
        console.log(
            "KERI_Auth_CS from extension **onMessage**: " +
            message.type +
            ":" +
            message.subtype
        );
        console.log(message); // as object
    }
});

function advertiseToPage(): void {
    console.log("KERI_Auth_CS to page: extensionId: " + chrome.runtime.id);
    window.postMessage(
        {
            type: "signify-extension",
            data: {
                extensionId: String(chrome.runtime.id)
            },
        },
        "*"
    );
}

// Delay in order for polaris-web module to be loaded and ready to receive the message.
// TODO find a more deterministic approach vs delay?
setTimeout(advertiseToPage, 1000);

// Send a message to the service worker, example
port.postMessage({ greeting: "hello" });

// Listen for messages from the service worker
port.onMessage.addListener((message: any) => {
    console.log("KERI_Auth_CS from extension:", message);
});