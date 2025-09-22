/// <reference types="chrome-types" />

import type * as PW from '../types/polaris-web-client';

import type {
    IReplyMessageData,
    IApprovedSignRequest
} from '../es6/ExCsInterfaces.js';
import {
    BwCsMsgEnum
} from '../es6/ExCsInterfaces.js';

interface DotNetObjectReference<T = any> {
    invokeMethodAsync: (methodName: string, ...args: any[]) => Promise<void>;
}

export const SwAppInteropModule = {
    initializeMessaging (dotNetObjectReference: DotNetObjectReference, tabId: String): chrome.runtime.Port | null {
        try {
            if (!tabId || typeof tabId !== 'string') {
                console.error('Invalid tabId provided');
                return null;
            }

            console.log('Initializing messaging for tab:', tabId);

            const port = chrome.runtime.connect('', { name: 'blazorAppPort' + `-tab-${  tabId}` });

            port.onMessage.addListener((message) => {
                console.log('SwAppInterop received port message: ', message);
                // TODO P2 message types fromApp vs fromServiceWorker?
                if (message && message.type === BwCsMsgEnum.FSW) {
                    dotNetObjectReference.invokeMethodAsync('ReceiveMessage', message.data);
                }
            });

            return port;
        } catch {
            console.error('SwAppInteropModule: Error initializing messaging');
            return null;
        }
    },

    sendMessageToServiceWorker (port: chrome.runtime.Port, jsonReplyMessageData: string): void {
        console.log('SwAppInteropModule.sendMessageToServiceWorker... ');

        try {
            const messageData = JSON.parse(jsonReplyMessageData) as IReplyMessageData<unknown>;
            console.log('SwAppInteropModule.sendMessageToServiceWorker messageData: ', messageData);
            const { type, requestId, payload, error, payloadTypeName, source } = messageData;

            // depending on type, re-parse and process
            switch (payloadTypeName) {
                case 'CancelResult':
                    // Note that AuthorizeResult type is the closest match to CancelResult
                    const msgCancelResult = JSON.parse(jsonReplyMessageData) as IReplyMessageData<PW.AuthorizeResult>;
                    console.log('SwAppInteropModule.sendMessageToServiceWorker messageData3: ', msgCancelResult);
                    port.postMessage(msgCancelResult);
                    break;
                case 'AuthorizeResult':
                    const msgAuthorizeResult = JSON.parse(jsonReplyMessageData) as IReplyMessageData<PW.AuthorizeResult>;
                    console.log('SwAppInteropModule.sendMessageToServiceWorker messageData2: ', msgAuthorizeResult);
                    port.postMessage(msgAuthorizeResult);
                    break;
                case 'ApprovedSignRequest':
                    const msgApprovedSignRequest = JSON.parse(jsonReplyMessageData) as IApprovedSignRequest;
                    console.log('SwAppInteropModule approvedSignRequest: ', msgApprovedSignRequest);
                    port.postMessage(msgApprovedSignRequest);
                    break;
                case 'SignedRequestResult':
                    const msgSignRequestResult = JSON.parse(jsonReplyMessageData) as IReplyMessageData<PW.SignRequestResult>;
                    console.log('SwAppInteropModule.sendMessageToServiceWorker messageData5: ', msgSignRequestResult);
                    port.postMessage(msgSignRequestResult);
                    break;
                case 'SignDataResult':
                case 'ConfigureVendorResult':
                default:
                    throw new Error(`SwAppInteropModule: unknown typeName: ${  payloadTypeName}`);
            }

            //const messageDataObject = { type, requestId, payloadObject, error }
            //console.log("SwAppInteropModule.sendMessageToServiceWorker messageDataObject: ", messageDataObject);
            //port.postMessage(messageDataObject);

        } catch (error) {
            console.error('SwAppInteropModule.sendMessageToServiceWorker error: ', error);
        }
    },

    /**
    * Safely parses a JSON string into a strongly-typed object.
    *
    * @param jsonString - The JSON string to parse.
    * @returns The parsed object of type T or null if parsing fails.
    */
    parseJson <T>(jsonString: string): T | null {
        try {
            const parsedObj: T = JSON.parse(jsonString);
            return parsedObj;
        } catch (error) {
            console.error('Error parsing JSON:', error);
            return null;
        }
    }

};

export default SwAppInteropModule;
