﻿/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

// import { IMessage, IBaseMsg, ICsSwMsg, IExCsMsg } from "./CommonInterfaces";
// TODO these are replicated from CommonInterfaces.ts, should be imported from there. However, this leads to typescript config issues that need to also be resolved.

// Common definitions for this content script and the extension service-worker.
// Note these are manually repeated here and in the ContentScript,
// because of the CommonJS module system that must be used for this ContentScript.
// A fix would be to use a separate CommonInterface.ts file and a bundler to build the content script, but that is not yet implemented.
//

// Message types from CS to SW
type CsSwMsg = ICsSwMsgSelectIdentifier | ICsSwMsgSelectCredential;
interface ICsSwMsg {
    type: string
}

enum CsSwMsgType {
    SELECT_IDENTIFIER = "select-identifier",
    SELECT_CREDENTIAL = "select-credential",
    SIGNIFY_EXTENSION = "signify-extension",
    SELECT_ID_CRED = "select-aid-or-credential",
    SELECT_AUTO_SIGNIN = "select-auto-signin",
    NONE = "none",
    VENDOR_INFO = "vendor-info",
    FETCH_RESOURCE = "fetch-resource",
    AUTO_SIGNIN_SIG = "auto-signin-sig",
    DOMCONTENTLOADED = "document-loaded"
}

interface ICsSwMsgSelectIdentifier extends ICsSwMsg {
    type: CsSwMsgType.SELECT_IDENTIFIER
}

interface ICsSwMsgHello extends ICsSwMsg {
    type: CsSwMsgType.DOMCONTENTLOADED
}

interface ICsSwMsgSelectCredential extends ICsSwMsg {
    type: CsSwMsgType.SELECT_CREDENTIAL
    data: any
}

// Message types from Extension to CS
interface IExCsMsg {
    type: string
}

enum ExCsMsgType {
    HELLO = "hello",
    BBB = "bbb",
}

interface IExCsMsgHello extends IExCsMsg {
    type: ExCsMsgType.HELLO
}
interface IExCsMsgBbb extends IExCsMsg {
    type: ExCsMsgType.BBB
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

// Generate a unique and unguessable identifier for the port name for communications between the content script and the extension
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => ('00000000' + dec.toString(16)).slice(-8)).join('-');
}

// Create the unique port name using the generated identifier
const uniquePortName: string = generateUniqueIdentifier();

const port = chrome.runtime.connect({ name: uniquePortName });

function advertiseToPage(): void {
    const advertizeMsg = {
        type: "signify-extension",
        data: {
            extensionId: String(chrome.runtime.id)
        },
    };
    console.log("KERI_Auth_CS to page: advertise", advertizeMsg);
    window.postMessage(
        advertizeMsg,
        "*"
    );
}

document.addEventListener('DOMContentLoaded', (event) => {
    console.log("KERI_Auth_CS: DOMContentLoaded event:", event);
    // ensure your content script responds to changes in the page even if it was injected after the page load.

    // Send a message to the service worker
    const helloMsg: ICsSwMsg = { name: "Hello from content script!", name2: "tmp" };
    console.log("KERI_Auth_CS: to SW:", helloMsg);
    port.postMessage(helloMsg);

    // register to receive and handle messages from the extension
    port.onMessage.addListener((message: IExCsMsg) => {
        // TODO move this into its own function for readability
        // Handle messages from the extension
        console.log("KERI_Auth_CS from extension:", message);
        // TODO confirm type of message and handle accordingly
        console.warn("KERI_Auth_CS from extension: message handlers not yet implemented");

        switch (message.name) {
            case "Hello from service worker!":
            default:
                console.log("KERI_Auth_CS from extension: default:", message);
                // Register to handle messages from web page, most of which will be forwarded to the extension service worker via the port
                window.addEventListener(
                    "message",
                    async (event: MessageEvent<EventData>) => {
                        // Accept messages only from same window
                        if (event.source !== window) {
                            return;
                        }
                        console.log("KERI_Auth_CS from page:", event.data);

                        switch (event.data.type) {
                            case "signify-extension":
                                var msg: ICsSwMsg = {
                                    name: "PageReady",
                                    name2: "tmp"
                                };
                                console.log("KERI_Auth_CS to extension:", msg);
                                port.postMessage(msg);
                                break;
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
                                var msg2: IMessage = {
                                    name: String(event.data.type)
                                };
                                console.log("KERI_Auth_CS to extension:", msg2);
                                port.postMessage(msg2);
                                break;
                        }
                    }
                );
        }
    });

    // Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
    // TODO find a more deterministic approach vs delay?
    // setTimeout(advertiseToPage, 1000);
    advertiseToPage();
});