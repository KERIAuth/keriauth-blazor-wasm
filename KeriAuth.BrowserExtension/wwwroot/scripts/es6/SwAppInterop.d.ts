interface DotNetObjectReference<T = any> {
    invokeMethodAsync: (methodName: string, ...args: any[]) => Promise<void>;
}
export interface ReplyMessageData<T = unknown> {
    type: string;
    requestId: string;
    payload?: T;
    error?: string;
    payloadTypeName?: string;
    source?: string;
}
export declare const SwAppInteropModule: {
    initializeMessaging: (dotNetObjectReference: DotNetObjectReference, tabId: String) => chrome.runtime.Port | null;
    sendMessageToServiceWorker: (port: chrome.runtime.Port, jsonReplyMessageData: string) => void;
    parseJson: <T>(jsonString: string) => T | null;
};
export default SwAppInteropModule;
