﻿/// <reference types="chrome" />

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

import { ICsSwMsgSelectIdentifier, CsSwMsgType, IExCsMsgHello, SwCsMsgType, ISwCsMsg, ICsSwMsg, CsToPageMsgIndicator, KeriAuthMessageData, ISignin, ICredential, ReplyMessageData } from "../es6/ExCsInterfaces.js";

interface DotNetObjectReference<T = any> {
    invokeMethodAsync: (methodName: string, ...args: any[]) => Promise<void>;
}

export const SwAppInteropModule = {
    initializeMessaging: function (dotNetObjectReference: DotNetObjectReference, tabId: String): chrome.runtime.Port | null {
        try {
            if (!tabId || typeof tabId !== "string") {
                console.error("Invalid tabId provided");
                return null;
            }

            console.log("Initializing messaging for tab:", tabId);

            const port = chrome.runtime.connect({ name: "blazorAppPort" + "-tab-" + tabId });

            port.onMessage.addListener((message) => {
                console.log("SwAppInterop received port message: ", message);
                // TODO EE! fromApp vs fromServiceWorker?
                if (message && message.type === 'fromServiceWorker') {
                    dotNetObjectReference.invokeMethodAsync('ReceiveMessage', message.data);
                }
            });

            return port;
        } catch {
            console.error("SwAppInteropModule: Error initializing messaging");
            return null;
        }
    },

    sendMessageToServiceWorker: function (port: chrome.runtime.Port, jsonReplyMessageData: string): void {
        console.log("SwAppInteropModule.sendMessageToServiceWorker... ");

        try {
            const messageData = JSON.parse(jsonReplyMessageData) as ReplyMessageData<unknown>;
            console.log("SwAppInteropModule.sendMessageToServiceWorker messageData: ", messageData);
            const { type, requestId, payload, error, payloadTypeName, source } = messageData;

            // depending on type, re-parse and process 
            switch (payloadTypeName) {
                case "AuthorizeResult":
                    const messageData2 = JSON.parse(jsonReplyMessageData) as ReplyMessageData<AuthorizeResult>;
                    // const maybeAuthorizeResult = parseJson<AuthorizeResult>(jsonReplyMessageData);
                    console.log("SwAppInteropModule.sendMessageToServiceWorker messageData2: ", messageData2);
                    port.postMessage(messageData);
                    break;
                case "SignDataResult":
                //const maybeAuthroizeResult = parseJson<AuthorizeResult>(jsonReplyMessageData);
                //if (maybeAuthroizeResult) {
                //    payloadObject = maybeAuthroizeResult;
                //} else {
                //    throw new Error('Invalid payloadJson for SignDataResult');
                //}
                // break;
                case "SignDataResult":
                
                case "void":
                default:
                    throw new Error('Unknown typeName: ' + payloadTypeName);
            }

            //const messageDataObject = { type, requestId, payloadObject, error }
            //console.log("SwAppInteropModule.sendMessageToServiceWorker messageDataObject: ", messageDataObject);
            //port.postMessage(messageDataObject);

        } catch (error) {
            console.error("SwAppInteropModule.sendMessageToServiceWorker error: ", error);
        }
    },

    /**
    * Safely parses a JSON string into a strongly-typed object.
    * 
    * @param jsonString - The JSON string to parse.
    * @returns The parsed object of type T or null if parsing fails.
    */
    parseJson: function <T>(jsonString: string): T | null {
        try {
            const parsedObj: T = JSON.parse(jsonString);
            return parsedObj;
        } catch (error) {
            console.error("Error parsing JSON:", error);
            return null;
        }
    }

};

export default SwAppInteropModule;
