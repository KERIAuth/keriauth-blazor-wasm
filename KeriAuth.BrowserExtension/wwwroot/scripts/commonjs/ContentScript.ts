﻿/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

// import { IMessage, IBaseMsg, ICsSwMsg, IExCsMsg } from "./CommonInterfaces";
// TODO these are replicated from CommonInterfaces.ts, should be imported from there. However, this leads to typescript config issues that need to also be resolved.
interface IMessage {
    name: string
}

interface ICsSwMsg extends IMessage {
    name2: string,
}

interface IExCsMsg extends IMessage {
    name3: string,
}

// signify-brower-extension compliant page message types
// Note this is called TAB_STATE and others in the signify-browser-extension
// this "const" structure is intentionally used versus an enum, because of CommonJS module system in use

const PAGE_EVENT_TYPE = Object.freeze({
    SELECT_IDENTIFIER: "select-identifier",
    SELECT_CREDENTIAL: "select-credential",
    SELECT_ID_CRED: "select-aid-or-credential",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    NONE: "none",
    VENDOR_INFO: "vendor-info",
    FETCH_RESOURCE: "fetch-resource",
    AUTO_SIGNIN_SIG: "auto-signin-sig",
})

const PAGE_POST_TYPE = Object.freeze({
    SIGNIFY_EXT: "signify-extension",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    SIGNIFY_SIGNATURE: "signify-signature",
    SIGNIFY_SIGNATURE2: "signify-signature",
})

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
    // type: typeof PAGE_POST_TYPE;
}

interface SignifyExtensionMessage extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SIGNIFY_EXT;
    data: {
        extensionId: string;
    };
}

interface SignifySignatureMessage extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SIGNIFY_SIGNATURE;
    data: any;
    requestId: string;
    rurl: string;
}

interface SignifyAutoSigninMessage extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SELECT_AUTO_SIGNIN;
    requestId: string;
    rurl: string;
}

// Union type for all possible messages that can be sent to the web page
type PageMessage = SignifyExtensionMessage | SignifySignatureMessage | SignifyAutoSigninMessage;

// Connect with the extension service worker via a port, using the tabId as the port name.
// First, get the tabId from the extension service worker
var tabId: number = 0;
var msg: IMessage = {
    name: "getTabId",
};
var port: chrome.runtime.Port;
console.log("KERI_Auth_CS to extension:", msg);
chrome.runtime.sendMessage(msg, (response) => {
    // Now we have the response from the extension service worker containing the page's tabId
    // TODO error or confirm response can be converted to a number
    tabId = Number(response);
    console.log("KERI_Auth_CS from extension: tabId:", tabId);
    port = Object.freeze(chrome.runtime.connect({ name: String(tabId) }));

    // Listen for and handle messages from the service worker
    port.onMessage.addListener((message: any) => {
        console.log("KERI_Auth_CS from extension:", message);
        // TODO confirm type of message and handle accordingly
        console.warn("KERI_Auth_CS from extension: message handlers not yet implemented");
    });

    // Handle messages from web page, most of which will be forwarded to the extension service worker via the port
    window.addEventListener(
        "message",
        async (event: MessageEvent<EventData>) => {
            // Accept messages only from same window
            if (event.source !== window) {
                return;
            }
            console.log("KERI_Auth_CS from page:", event.data);

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
                    // TODO implement real message types, with in-out mappings.
                    var msg: IMessage = {
                        name: String(event.data.type)
                    };
                    console.log("KERI_Auth_CS to extension:", msg);
                    port.postMessage(msg);
                    return;
            }
        }
    );
});

// Handle non-port messages from extension to content script
chrome.runtime.onMessage.addListener(async function (
    message: ChromeMessage,
    sender: chrome.runtime.MessageSender,
    sendResponse: (response?: any) => void) {
    if (sender.id === chrome.runtime.id) {
        console.warn("KERI_Auth_CS from extension: is this onMessage handler needed?");
        console.log("KERI_Auth_CS from extension **onMessage**:", message);
    }
});

function advertiseToPage(): void {
    console.log("KERI_Auth_CS to page: extensionId:", chrome.runtime.id);
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

// Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
// TODO find a more deterministic approach vs delay?
setTimeout(advertiseToPage, 1000);