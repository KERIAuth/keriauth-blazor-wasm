/// <reference types="chrome-types" />

import type * as PW from '../types/polaris-web-client';

import type {
    IReplyMessageData,
    IApprovedSignRequest
} from '../es6/ExCsInterfaces.js';
import {
    BwCsMsgEnum
} from '../es6/ExCsInterfaces.js';

interface IDotNetObjectReference {
    invokeMethodAsync: (methodName: string, ...args: unknown[]) => Promise<void>;
}

// Generate unique identifier for port names
function generateUniqueIdentifier(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => (`00000000${dec.toString(16)}`).slice(-8)).join('-');
}

export const SwAppInteropModule = {
    initializeMessaging (dotNetObjectReference: IDotNetObjectReference, contextType: string): chrome.runtime.Port | null {
        try {
            if (!contextType || typeof contextType !== 'string') {
                console.error('Invalid contextType provided');
                return null;
            }

            console.log('Initializing messaging for context:', contextType);

            // Generate port name based on context type
            let portPrefix: string;
            switch (contextType.toUpperCase()) {
                case 'TAB':
                    portPrefix = 'BA_TAB';
                    break;
                case 'POPUP':
                    portPrefix = 'BA_POPUP';
                    break;
                case 'SIDEPANEL':
                    portPrefix = 'BA_SIDEPANEL';
                    break;
                default:
                    portPrefix = 'BA_UNKNOWN';
                    console.warn('Unknown context type, using default prefix:', contextType);
            }

            const portName = `${portPrefix}|${generateUniqueIdentifier()}`;
            console.log('Creating port with name:', portName);

            const port = chrome.runtime.connect('', { name: portName });

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

    sendMessageToBackgroundWorker (port: chrome.runtime.Port, jsonReplyMessageData: string): void {
        console.log('SwAppInteropModule.sendMessageToBackgroundWorker... ');

        try {
            const messageData = JSON.parse(jsonReplyMessageData) as IReplyMessageData<unknown>;
            console.log('SwAppInteropModule.sendMessageToBackgroundWorker messageData: ', messageData);
            const { type: _type, requestId: _requestId, payload: _payload, error: _error, payloadTypeName, source: _source } = messageData;

            // depending on type, re-parse and process
            switch (payloadTypeName) {
                case 'CancelResult': {
                    // Note that AuthorizeResult type is the closest match to CancelResult
                    const msgCancelResult = JSON.parse(jsonReplyMessageData) as IReplyMessageData<PW.AuthorizeResult>;
                    console.log('SwAppInteropModule.sendMessageToBackgroundWorker messageData3: ', msgCancelResult);
                    port.postMessage(msgCancelResult);
                    break;
                }
                case 'AuthorizeResult': {
                    const msgAuthorizeResult = JSON.parse(jsonReplyMessageData) as IReplyMessageData<PW.AuthorizeResult>;
                    console.log('SwAppInteropModule.sendMessageToBackgroundWorker messageData2: ', msgAuthorizeResult);
                    port.postMessage(msgAuthorizeResult);
                    break;
                }
                case 'ApprovedSignRequest': {
                    const msgApprovedSignRequest = JSON.parse(jsonReplyMessageData) as IApprovedSignRequest;
                    console.log('SwAppInteropModule approvedSignRequest: ', msgApprovedSignRequest);
                    port.postMessage(msgApprovedSignRequest);
                    break;
                }
                case 'SignedRequestResult': {
                    const msgSignRequestResult = JSON.parse(jsonReplyMessageData) as IReplyMessageData<PW.SignRequestResult>;
                    console.log('SwAppInteropModule.sendMessageToServiceWorker messageData5: ', msgSignRequestResult);
                    port.postMessage(msgSignRequestResult);
                    break;
                }
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
