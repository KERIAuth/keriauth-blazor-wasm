interface DotNetObjectReference<T = any> {
    invokeMethodAsync: (methodName: string, ...args: any[]) => Promise<void>;
}
export declare const SwAppInteropModule: {
    initializeMessaging: (dotNetObjectReference: DotNetObjectReference, tabId: String) => chrome.runtime.Port | null;
    sendMessageToServiceWorker: (port: chrome.runtime.Port, jsonReplyMessageData: string) => void;
    parseJson: <T>(jsonString: string) => T | null;
};
export default SwAppInteropModule;
