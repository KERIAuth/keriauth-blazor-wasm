/// <reference types="chrome" />

// This ContentScript is inserted into pages after a user provided permission for the site (after having clicked on the extension action button)
// The purpose of the ContentScript is primarily to shuttle messages from a web page to/from the extension service-worker.

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

import { ICsSwMsgSelectIdentifier, CsSwMsgType, IExCsMsgHello, ExCsMsgType, IExCsMsg, ICsSwMsg } from "../es6/ExCsInterfaces.js";
import {
    AuthorizeResultCredential,
    AuthorizeArgs,
    AuthorizeResultIdentifier,
    AuthorizeResult,
    SignDataArgs,
    SignDataResultItem,
    SignDataResult,
    SignRequestArgs,
    SignRequestResult,
    ConfigureVendorArgs,
    MessageData
} from "polaris-web/dist/client";


// signify-brower-extension compliant page message types
// Note this is called TAB_STATE and others in the signify-browser-extension
// this "const" structure was intentionally used versus an enum, because of CommonJS module system in use.
// TODO above is no longer a constraint, and can move these back to enums?

// page to CS
const PAGE_EVENT_TYPE = Object.freeze({
    VENDOR_INFO: "vendor-info",
    FETCH_RESOURCE: "fetch-resource",
    SELECT_IDENTIFIER: "/signify/authorize/aid",
    SELECT_CREDENTIAL: "/signify/authorize/credential",
    SELECT_ID_CRED: "/signify/authorize",
    AUTHORIZE_AUTO_SIGNIN: "/signify/authorize-auto-signin",
    SIGN_REQUEST: "/signify/sign-request",
    CONFIGURE_VENDOR: "/signify/configure-vendor",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    NONE: "none"
})

// CS to page
const PAGE_POST_TYPE = Object.freeze({
    SIGNIFY_EXT: "signify-extension"
    // SELECT_AUTO_SIGNIN: "select-auto-signin",
    // SIGNIFY_SIGNATURE: "signify-signature",
    // SIGNIFY_SIGNATURE2: "signify-signature"
})

// interfaces for the messages posted to the web page
interface BaseMessage {
    version: string
    type: string
}

interface SignifyExtensionMessage extends BaseMessage {
    type: typeof PAGE_POST_TYPE.SIGNIFY_EXT
    version: "0.0.1"
    data: {
        extensionId: string
    };
}

//interface SignifySignatureMessage extends BaseMessage {
//    type: typeof PAGE_POST_TYPE.SIGNIFY_SIGNATURE
//    version: "0.0.1"
//    data: any
//    requestId: string
//    rurl: string
//}

//interface SignifyAutoSigninMessage extends BaseMessage {
//    type: typeof PAGE_POST_TYPE.SELECT_AUTO_SIGNIN
//    version: "0.0.1"
//    requestId: string
//    rurl: string
//}

// Union type for all possible messages that can be sent to the web page
// type PageMessage = SignifyExtensionMessage | SignifySignatureMessage | SignifyAutoSigninMessage;

// Function to generate a unique and unguessable identifier for the port name for communications between the content script and the extension
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => ('00000000' + dec.toString(16)).slice(-8)).join('-');
}

// Create the unique port name for communications between the content script and the extension
const uniquePortName: string = generateUniqueIdentifier();

function advertiseToPage(): void {
    const advertizeMsg = {
        type: "signify-extension",
        data: {
            extensionId: String(chrome.runtime.id)
        },
    };
    console.log("KeriAuthCs to page:", advertizeMsg);
    window.postMessage(
        advertizeMsg,
        "*"
    );
}

// ensure your content script responds to changes in the page even if it was injected after the page load.
document.addEventListener('DOMContentLoaded', (event) => {
    console.log("KeriAuthCs DOMContentLoaded event:", event);

    console.log("KeriAuthCs DOMContentLoaded connecting to :", uniquePortName);
    const port = chrome.runtime.connect(/* TODO extensionId: string , */  { name: uniquePortName });
    console.log("KeriAuthCs DOMContentLoaded connected:", port);

    // register to receive and handle messages from the extension, and then from the page
    port.onMessage.addListener((message: IExCsMsg) => {
        // TODO move this into its own function for readability
        // Handle messages from the extension
        console.log("KeriAuthCs from SW:", message);
        switch (message.type) {
            case ExCsMsgType.HELLO:

                // Register to handle messages from web page, most of which will be forwarded to the extension service worker via the port
                window.addEventListener(
                    "message",
                    async (event: MessageEvent<EventData>) => {
                        // Reject likely malicious messages, such as those that might be sent by a malicious extension (Cross-Extension Communication).
                        // Check if the origin matches the origin of the current page
                        if (event.origin !== window.origin) {
                            console.warn("KeriAuthCs from page: event: ", event);
                            console.warn('Message origin mismatch. Ignoring message.');
                            return;
                        }
                        // Ensure the message is from the page's window
                        if (event.source !== window) {
                            console.warn("KeriAuthCs from page: event: ", event);
                            console.warn('Message source mismatch. Ignoring message.');
                            return;
                        }
                        // Optionally, verify that the message data contains an expected format or token
                        //if (!event.data || event.data.token !== 'expectedTokenValue') {
                        //    console.warn('Message data mismatch or missing token. Ignoring message.');
                        //    return;
                        //}

                        // Handle messages from the page
                        console.log("KeriAuthCs from page:", event.data);

                        try {
                            switch (event.data.type) {
                                case "signify-extension":
                                    // Note, this notification to SW is effectively haneled earlier in the code, in the advertiseToPage function
                                    console.log("KeriAuthCs: message ignored:", event.data);
                                    break;
                                case PAGE_EVENT_TYPE.SELECT_ID_CRED:
                                    try {
                                        if (event.data && event.data.payload && event.data.payload.message) {
                                            const message: string = event.data.payload.message;
                                            const msg2: ICsSwMsgSelectIdentifier = {
                                                type: CsSwMsgType.SELECT_IDENTIFIER,
                                                message: message
                                            }
                                            console.log("KeriAuthCs to SW:", msg2);
                                            port.postMessage(msg2);
                                        } else {
                                            throw new Error("Invalid JSON structure: 'message' field not found. Or, issue sending to SW");
                                        }
                                    } catch (error) {
                                        // Handle any errors that may occur during parsing or property access
                                        console.error("An error occurred:", (error as Error).message);
                                        return;
                                    }
                                    break;
                                case PAGE_EVENT_TYPE.SELECT_CREDENTIAL:
                                case PAGE_EVENT_TYPE.SELECT_IDENTIFIER:
                                case PAGE_EVENT_TYPE.SELECT_AUTO_SIGNIN:
                                case PAGE_EVENT_TYPE.NONE:
                                case PAGE_EVENT_TYPE.VENDOR_INFO:
                                case PAGE_EVENT_TYPE.FETCH_RESOURCE:
                                default:
                                    console.error("KeriAuthCs from page: handler not yet implemented for:", event.data);
                                    break;
                            }
                        } catch (error) {
                            console.error("KeriAuthCs from page: error in handling event: ", event.data, "Extension may have been reloaded. Try reloading page.", "Error:", error)
                        }
                    }

                );
                break;
            case ExCsMsgType.FSW:
                console.info("KeriAuthCs from SW: handler not implemented for message type:", message.type);
                break;
            case ExCsMsgType.CANCELED:
            case ExCsMsgType.SIGNED:
            default:
                console.error("KeriAuthCs from SW: handler not implemented for message type:", message.type);
                break;
        }
    });

    // Send a hello message to the service worker (versus waiting on a trigger message from the page)
    const helloMsg: ICsSwMsg = { type: CsSwMsgType.SIGNIFY_EXTENSION };
    console.log("KeriAuthCs to SW:", helloMsg);
    port.postMessage(helloMsg);

    // Delay call of advertiseToPage so that polaris-web module to be loaded and ready to receive the message.
    // TODO find a more deterministic approach vs delay?
    setTimeout(advertiseToPage, 500);
    // advertiseToPage();
});