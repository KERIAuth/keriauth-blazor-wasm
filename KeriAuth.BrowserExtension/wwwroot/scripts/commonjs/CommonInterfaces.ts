// TODO these are replicated in ContentScript.ts and should be imported from there. However, this leads to typescript config issues that need to also be resolved.
export interface IMessage {
    name: string,
    sourceHostname: string;
    sourceOrigin: string;
    windowId: number;
}

export interface IBaseMsg {
    name: string,
}

export interface ICsSwMsg {
    name: string,
}

export interface IExCsMsg {
    name: string,
}