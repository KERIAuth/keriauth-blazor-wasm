// Note the compilation of this .ts file is bundled with its dependencies.  See entry in package.json for its build.
// This Javascript-C# interop layer is paired with Signify_ts_shim.cs

// See the following for more inspiration:
// https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/pages/background/services/signify.ts

import {
    SignifyClient,
    Tier,
    ready,
    Authenticater,
    randomPasscode,
    EventResult,
    Identifier,
} from "signify-ts";

export const PASSCODE_TIMEOUT = 5;

let _client: SignifyClient | null;

/**
 * Connect or boot a SignifyClient instance
 * @param agentUrl
 * @param bootUrl
 * @param passcode
 * @returns
 */
// rename to getOrCreateClient?  See https://github.com/WebOfTrust/signify-ts/blob/fddaff20f808b9ccfed517b3a38bef3276f99261/examples/integration-scripts/utils/test-setup.ts#L13
export const bootAndConnect = async (
    agentUrl: string,
    bootUrl: string,
    passcode: string
): Promise<string> => {
    _client = null;
    await ready();
    console.debug(`signify_ts_shim: bootAndConnect: creating client...`);
    _client = new SignifyClient(agentUrl, passcode.padEnd(21, '_'), Tier.low, bootUrl);

    try {
        await _client.connect();
        console.debug("signify_ts_shim: client connected");
    } catch {
        const res = await _client.boot();
        if (!res.ok) throw new Error();
        await _client.connect();
        console.debug("signify_ts_shim: client booted and connected");
    }
    console.log('signify_ts_shim: client', {
        agent: _client.agent?.pre,
        controller: _client.controller.pre
    });
    const state = await getState();
    console.debug(`signify_ts_shim: bootAndConnect: connected`);
    console.assert(state?.controller?.state?.i != null, "controller id is null"); // TODO throw exception?

    return objectToJson(_client);
};

const objectToJson = (obj: object): string => {
    return JSON.stringify(obj);
}

const validateClient = () => {
    if (!_client) {
        throw new Error("signify_ts_shim: Client not connected");
    }
};
const getState = async () => {
    validateClient();
    return await _client?.state();
};

export const connect = async (agentUrl: string, passcode: string): Promise<string> => {
    _client = null;
    await ready();
    console.debug(`signify_ts_shim: connect: creating client...`);
    _client = new SignifyClient(agentUrl, passcode, Tier.low, "");

    try {
        await _client.connect();
        console.debug("signify_ts_shim: client connected");
    } catch {
        console.error("signify_ts_shim: client could not connect");
    }

    const state = await getState();
    console.debug(`signify_ts_shim: connect: connected`);
    console.assert(state?.controller?.state?.i != null, "controller id is null"); // TODO throw exception?

    return objectToJson(_client);
};

// Type guard to verify if an object is a SignifyClient or something close to it
function isClient(obj: any): obj is SignifyClient {
    return (
        typeof obj === "object" &&
        typeof obj.controller === "object" &&
        typeof obj.url === "string" &&
        typeof obj.bran === "string"
    )
}

// see also https://github.com/WebOfTrust/signify-ts/blob/fddaff20f808b9ccfed517b3a38bef3276f99261/examples/integration-scripts/utils/test-setup.ts
export async function createAID(
    name: string
): Promise<string> {
    try {
        validateClient();
        // TODO P3 consider adding a check for the client's state to ensure it is connected
        // await _client.connect();
        // console.debug("signify_ts_shim: client connected");
        const client: SignifyClient = _client!;
        const res: EventResult = await client.identifiers().create(name);
        const op2 = await res.op();
        let id: string = op2.response.i;
        console.log("signify_ts_shim: createAID id: " + id);
        return id;
        // TODO expand to also return the OOBI.  See test-setup.ts
    }
    catch (error) {
        console.error(error);
        throw error;
    }
};

export const getAIDs = async () => {
    validateClient();
    const client: SignifyClient = _client!;
    const managedIdentifiers = await client.identifiers().list();
    // TODO: unclear what should be returned and its type
    let identifierJson: string = JSON.stringify(managedIdentifiers);
    console.debug("signify_ts_shim: getAIDs: ", managedIdentifiers);
    return identifierJson;
}     


export const getAID = async (id: string) => {
    validateClient();
    const client: SignifyClient = _client!;
    const managedIdentifier = await client.identifiers().get(id);
    let identifierJson: string = JSON.stringify(managedIdentifier);
    console.debug("signify_ts_shim: getAID: ", managedIdentifier);
    return identifierJson;
}
