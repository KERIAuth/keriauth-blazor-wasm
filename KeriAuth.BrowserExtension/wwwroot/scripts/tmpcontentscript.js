// Handle messages from web page
window.addEventListener(
    "message",
    async (event) => {
        // Accept messages only from same window
        if (event.source !== window) {
            return;
        }
        console.log("KERI Auth content script received message from page: " + event.data.type); // JSON.stringify(event.data));
        console.log(event.data);
        return;
    });


// Handle messages from background script and popup
chrome.runtime.onMessage.addListener(async function (
    message,
    sender,
    sendResponse
) {
    if (sender.id === chrome.runtime.id) {
        console.log(
            "KERI Auth content script received message from extension: " +
            message.type +
            ":" +
            message.subtype);
    }
});

function advertize() { 
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

// Advertize extensionId to web page.
// Note this might be consumed by the web page to identify the extension and/or its polaris-web component. So, sending it twice?
// Delay in order for polaris-web to be loaded and be able to receive the message
setTimeout(advertize, 1000);