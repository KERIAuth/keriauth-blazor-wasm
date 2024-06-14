"use strict";
/// <reference types="chrome" />
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
// Handle messages from web page
window.addEventListener("message", (event) => __awaiter(void 0, void 0, void 0, function* () {
    // Accept messages only from same window
    if (event.source !== window) {
        return;
    }
    console.log("KERI Auth content script received message from page: " + event.data.type);
    console.log(event.data);
    return;
}));
// Handle messages from extension
chrome.runtime.onMessage.addListener(function (message, sender, sendResponse) {
    return __awaiter(this, void 0, void 0, function* () {
        if (sender.id === chrome.runtime.id) {
            console.log("KERI Auth content script received message from extension: " +
                message.type +
                ":" +
                message.subtype);
        }
    });
});
function advertize() {
    console.log("KERI Auth content script advertized its extensionId: " + chrome.runtime.id);
    window.postMessage({
        type: "signify-extension",
        data: {
            extensionId: String(chrome.runtime.id)
        },
    }, "*");
}
// Delay in order for polaris-web module to be loaded and ready to receive the message.
// TODO find a more deterministic approach vs delay?
setTimeout(advertize, 1000);
