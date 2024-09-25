import { SwCsMsgType } from "../es6/ExCsInterfaces.js";
export const SwAppInteropModule = {
    initializeMessaging: function (dotNetObjectReference, tabId) {
        try {
            if (!tabId || typeof tabId !== "string") {
                console.error("Invalid tabId provided");
                return null;
            }
            console.log("Initializing messaging for tab:", tabId);
            const port = chrome.runtime.connect({ name: "blazorAppPort" + "-tab-" + tabId });
            port.onMessage.addListener((message) => {
                console.log("SwAppInterop received port message: ", message);
                if (message && message.type === SwCsMsgType.FSW) {
                    dotNetObjectReference.invokeMethodAsync('ReceiveMessage', message.data);
                }
            });
            return port;
        }
        catch {
            console.error("SwAppInteropModule: Error initializing messaging");
            return null;
        }
    },
    sendMessageToServiceWorker: function (port, jsonReplyMessageData) {
        console.log("SwAppInteropModule.sendMessageToServiceWorker... ");
        try {
            const messageData = JSON.parse(jsonReplyMessageData);
            console.log("SwAppInteropModule.sendMessageToServiceWorker messageData: ", messageData);
            const { type, requestId, payload, error, payloadTypeName, source } = messageData;
            switch (payloadTypeName) {
                case "CancelResult":
                    const messageData3 = JSON.parse(jsonReplyMessageData);
                    console.log("SwAppInteropModule.sendMessageToServiceWorker messageData2: ", messageData3);
                    port.postMessage(messageData3);
                    break;
                case "AuthorizeResult":
                    const messageData2 = JSON.parse(jsonReplyMessageData);
                    console.log("SwAppInteropModule.sendMessageToServiceWorker messageData2: ", messageData2);
                    port.postMessage(messageData2);
                    break;
                case "SignDataResult":
                case "SignDataResult":
                case "ConfigureVendorResult":
                case "void":
                default:
                    throw new Error('Unknown typeName: ' + payloadTypeName);
            }
        }
        catch (error) {
            console.error("SwAppInteropModule.sendMessageToServiceWorker error: ", error);
        }
    },
    parseJson: function (jsonString) {
        try {
            const parsedObj = JSON.parse(jsonString);
            return parsedObj;
        }
        catch (error) {
            console.error("Error parsing JSON:", error);
            return null;
        }
    }
};
export default SwAppInteropModule;
//# sourceMappingURL=SwAppInterop.js.map