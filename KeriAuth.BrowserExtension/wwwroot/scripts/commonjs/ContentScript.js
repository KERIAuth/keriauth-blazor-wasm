"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
const PAGE_EVENT_TYPE = Object.freeze({
    SELECT_IDENTIFIER: "select-identifier",
    SELECT_CREDENTIAL: "select-credential",
    SELECT_ID_CRED: "select-aid-or-credential",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    NONE: "none",
    VENDOR_INFO: "vendor-info",
    FETCH_RESOURCE: "fetch-resource",
    AUTO_SIGNIN_SIG: "auto-signin-sig",
});
const PAGE_POST_TYPE = Object.freeze({
    SIGNIFY_EXT: "signify-extension",
    SELECT_AUTO_SIGNIN: "select-auto-signin",
    SIGNIFY_SIGNATURE1: "signify-signature",
    SIGNIFY_SIGNATURE2: "signify-signature",
});
var myTabId = 0;
var mm = {
    name: "getTabId",
    sourceHostname: "",
    sourceOrigin: "",
    windowId: 0,
};
chrome.runtime.sendMessage(mm, (response) => {
    console.log("KERI_Auth_CS to extension: tabId: ", response);
    myTabId = Number(response);
});
console.log("tabId: ", myTabId);
const port = chrome.runtime.connect({ name: String(myTabId) });
window.addEventListener("message", (event) => __awaiter(void 0, void 0, void 0, function* () {
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
}));
chrome.runtime.onMessage.addListener(function (message, sender, sendResponse) {
    return __awaiter(this, void 0, void 0, function* () {
        if (sender.id === chrome.runtime.id) {
            console.log("KERI_Auth_CS from extension **onMessage**: " +
                message.type +
                ":" +
                message.subtype);
            console.log(message);
        }
    });
});
function advertiseToPage() {
    console.log("KERI_Auth_CS to page: extensionId: " + chrome.runtime.id);
    window.postMessage({
        type: "signify-extension",
        data: {
            extensionId: String(chrome.runtime.id)
        },
    }, "*");
}
setTimeout(advertiseToPage, 1000);
port.postMessage({ greeting: "hello" });
port.onMessage.addListener((message) => {
    console.log("KERI_Auth_CS from extension:", message);
});
