/// <reference types="chrome" />



// import { IMessage } from "./CommonInterfaces";

// This ContentScript is inserted into pages after a user has clicked on the extension action button once and provided permissions for the site,
// The purpose of the ContentScript is primarily to shuttle messages from a web page
// to/from the extension service worker.

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

// Handle messages from web page
window.addEventListener(
    "message",
    async (event: MessageEvent<EventData>) => {
        // Accept messages only from same window
        if (event.source !== window) {
            return;
        }
        console.log("KERI Auth content script received message from page: " + event.data.type);
        console.log(event.data);
        return;
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
            "KERI Auth content script received message from extension: " +
            message.type +
            ":" +
            message.subtype
        );
    }
});

function advertize(): void {
    console.log("KERI Auth content script advertized its extensionId: " + chrome.runtime.id);
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
setTimeout(advertize, 1000);