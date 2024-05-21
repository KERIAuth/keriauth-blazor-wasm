export interface IWalletRequest {
    type: string;
    content: object;
    iframeMode: boolean;
}

export interface IMessage {
    name: string,
    walletRequest: IWalletRequest
    sourceHostname: string;
    sourceOrigin: string;
    contentHash: string;
    timestamp: number;
    windowId: number;
    iframeMode: boolean;
}

export interface IWalletRequestStorage {
    type: string;
    content: object;
    sourceHostname: string;
    sourceOrigin: string;
    timestamp: number;
}