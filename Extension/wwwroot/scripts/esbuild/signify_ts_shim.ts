/* eslint-disable @typescript-eslint/no-unused-vars */
/* eslint-disable no-unused-vars */
// Note the compilation of this .ts file is bundled with its dependencies.  See entry in package.json for its build.
// This Javascript-C# interop layer is paired with Signify_ts_shim.cs

// See the following for more inspiration:
// https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/pages/background/services/signify.ts

import type {
    EventResult
} from 'signify-ts';

// eslint-disable-next-line no-duplicate-imports
import {
    SignifyClient,
    Tier,
    ready
} from 'signify-ts';

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
    console.debug('signify_ts_shim: bootAndConnect: creating client...');
    // TODO P2 raise the Tier for production use
    _client = new SignifyClient(agentUrl, passcode, Tier.low, bootUrl);

    try {
        console.debug('signify_ts_shim: connecting...');
        await _client.connect();
    } catch {
        try {
            console.debug('signify_ts_shim: booting...');
            const bootedSignifyClient = await _client.boot();
            if (!bootedSignifyClient.ok) {
                throw new Error();
            }
            console.debug('signify_ts_shim: connecting...');
            await _client.connect();
        } catch (error) {
            console.error('signify_ts_shim: client could not boot or connect', error);
            throw error;
        }
    }
    // note that uncommenting the next line might expose the passkey
    // console.error('signify_ts_shim: client', {agent: _client.agent?.pre,controller: _client.controller.pre});
    const state = await getState();
    console.debug('signify_ts_shim: bootAndConnect: connected');
    console.assert(state?.controller?.state?.i !== null, 'controller id is null'); // TODO P2 throw exception?

    return objectToJson(_client);
};

const objectToJson = (obj: object): string => JSON.stringify(obj);

const validateClient = () => {
    if (!_client) {
        throw new Error('signify_ts_shim: Client not connected');
    }
};
const getState = async () => {
    validateClient();
    return await _client?.state();
};

export const connect = async (agentUrl: string, passcode: string): Promise<string> => {
    _client = null;
    await ready();
    console.debug('signify_ts_shim: connect: creating client...');
    _client = new SignifyClient(agentUrl, passcode, Tier.low, '');

    try {
        await _client.connect();
        console.debug('signify_ts_shim: client connected');
    } catch {
        console.error('signify_ts_shim: client could not connect');
    }

    const state = await getState();
    console.debug('signify_ts_shim: connect: connected');
    console.assert(state?.controller?.state?.i !== null, 'controller id is null'); // TODO P2 throw exception?

    return objectToJson(_client);
};

// Type guard to verify if an object is a SignifyClient or something close to it
/*
function isClient(obj: any): obj is SignifyClient {
    return (
        typeof obj === 'object' &&
        typeof obj.controller === 'object' &&
        typeof obj.url === 'string' &&
        typeof obj.bran === 'string'
    )
}
*/

// see also https://github.com/WebOfTrust/signify-ts/blob/fddaff20f808b9ccfed517b3a38bef3276f99261/examples/integration-scripts/utils/test-setup.ts
export async function createAID(
    name: string
): Promise<string> {
    try {
        validateClient();
        // TODO P3 consider adding a check for the client's state to ensure it is connected
        // await _client.connect();
        // console.debug("signify_ts_shim: client connected");
        const client: SignifyClient = _client as SignifyClient;
        const res: EventResult = await client.identifiers().create(name);
        const op2 = await res.op();
        const id: string = op2.response.i;
        // console.log("signify_ts_shim: createAID id: " + id);
        return id;
        // TODO P3 expand to also return the OOBI.  See test-setup.ts
    } catch (error) {
        console.error(error);
        throw error;
    }
};

export const getAIDs = async () => {
    validateClient();
    const client: SignifyClient = _client as SignifyClient;
    const managedIdentifiers = await client.identifiers().list();
    // TODO P3 unclear what should be returned and its type
    const identifierJson: string = JSON.stringify(managedIdentifiers);
    // console.log("signify_ts_shim: getAIDs: ", managedIdentifiers);
    return identifierJson;
};

// get AID by name, i.e., alias not prefix
export const getAID = async (name: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const managedIdentifier = await client.identifiers().get(name);
        const identifierJson: string = JSON.stringify(managedIdentifier);
        // console.log("signify_ts_shim: getAID: name, identifier:", name, managedIdentifier);
        return identifierJson;
    } catch (error) {
        console.error('signify_ts_shim: getAID: name, error:', name, error);
        throw error;
    }
};

export interface IIdentifier {
    name?: string;
    prefix: string;
}

export async function getNameByPrefix(prefix: string): Promise<string> {
    try {
        const aid = await getIdentifierByPrefix(prefix);
        return aid.name as string;
    } catch (error) {
        console.error('signify_ts_shim: getPrefixByName: prefix, error:', prefix, error);
        throw error;
    }
}

export async function getIdentifierByPrefix(prefix: string): Promise<IIdentifier> {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const identifiers = client.identifiers(); //.list();
        // console.warn("getNameByPrefix identifiers:", identifiers);
        const aid = await identifiers.get(prefix) as IIdentifier | undefined; // .find((i: any) => i.prefix === prefix) as IIdentifier;
        // console.warn("getNameByPrefix aid:", aid);
        if (!aid) {
            throw new Error(`Identifier with prefix ${prefix} not found`);
        }
        return aid;
    } catch (error) {
        console.error('signify_ts_shim: getIdentifierByPrefix: prefix, error:', prefix, error);
        throw error;
    }
}

export async function getCredentialsList(
    // filter: object
): Promise<string> {  // TODO P3 define the return type?
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const credentials: EventResult = await client.credentials().list();
        console.log('signify_ts_shim: getCredentialList credentials: ', credentials);
        const credentialsJson: string = JSON.stringify(credentials);
        return credentialsJson;
    } catch (error) {
        console.error(error);
        throw error;
    }
}

export async function getCredential(
    id: string,
    includeCESR: boolean = false
): Promise<any> {
    // TODO P3 define the return type.  Dictionary<string, object>?
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const credential = await client.credentials().get(id, includeCESR);
        console.log('signify_ts_shim: getCredential: ', credential);
        return credential as any;
    } catch (error) {
        console.error(error);
        throw error;
    }
}

// inspired by https://github.com/WebOfTrust/signify-browser-extension/blob/d51ba75a3258a7a29267044235b915e1d0444075/src/pages/background/services/signify.ts#L307
/**
   * @param origin - origin url from where request is being made -- required
   * @param url - resource url that the request is being made to -- required
   * @param method - http method of the request -- default GET
   * @param headersDict - initialHeaders object of the request -- default empty
   * @param signin - signin object containing identifier or credential -- required
   * @returns Promise<Request> - returns a signed initialHeaders request object
   */
export const getSignedHeaders = async (
    origin: string,
    url: string,
    method: string,
    headersDict: { [key: string]: string },
    aidName: string
): Promise<{ [key: string]: string }> => {
    console.log('signify_ts_shim getSignedHeaders: params: origin: ', origin, ' url:', url, ' method:', method, ' aidname:', aidName);

    console.log('signify_ts_shim getSignedHeaders: params: headers:...');
    for (const key in headersDict) {
        if (Object.prototype.hasOwnProperty.call(headersDict, key)) {
            console.log(`  Header: ${key} = ${headersDict[key]}`);
        }
    }

    validateClient();
    const client: SignifyClient = _client as SignifyClient;
    try {
        const requestInit: RequestInit = {
            method,
            headers: headersDict
        };
        const signedRequest: Request = await client.createSignedRequest(aidName, url, requestInit);
        console.log('signify_ts_shim getSignedHeaders: signedRequest:', signedRequest);

        // Log each header for better visibility
        if (signedRequest.headers) {
            console.info('signify_ts_shim getSignedHeaders: signedRequest.headers details:');
            signedRequest.headers.forEach((value, key) => {
                console.log(`    ${key}: ${value}`);
            });
        }

        const jsonHeaders: { [key: string]: string } = {};
        if (signedRequest?.headers) {
            for (const pair of signedRequest.headers.entries()) {
                jsonHeaders[pair[0]] = String(pair[1]);
            }
        }
        console.log('signify_ts_shim getSignedHeaders: jsonHeaders:', jsonHeaders);
        return jsonHeaders;

    } catch (error) {
        console.error('signify_ts_shim getSignedHeaders: Error occurred:', error);
        throw error;
    }
};

export function parseHeaders(headersJson: string | null): Headers {
    try {
        // If headersJson is null, return an empty Headers object
        if (!headersJson) {
            console.log('parseHeaders: null new Headers: ', new Headers());
            return new Headers();
        }

        // Try to parse the JSON string
        const headersObj: Record<string, string> = JSON.parse(headersJson);

        // Check if the parsed result is a plain object
        if (typeof headersObj !== 'object' || headersObj === null) {
            throw new Error('Invalid headers format');
        }

        // Convert the plain object to a Headers object
        console.log('parseHeaders: headersObj: ', headersObj, ' newHeaders: ', new Headers(headersObj));
        return new Headers(headersObj);
    } catch (error) {
        console.error('Failed to parse headersJson:', error);
        // Return an empty Headers object in case of failure
        return new Headers();
    }
}

function headersToJsonBase64(headers: Headers): string {
    const headersObject: { [key: string]: string } = {};
    headers.forEach((value, key) => {
        headersObject[key] = value;
    });
    const jsonString = JSON.stringify(headersObject);
    const base64String = Buffer.from(jsonString).toString('base64');
    return base64String;
}

// from https://github.com/WebOfTrust/signify-browser-extension/blob/d51ba75a3258a7a29267044235b915e1d0444075/src/config/types.ts
interface ISignin {
    id: string;
    domain: string;
    identifier?: {
        name?: string;
        prefix?: string;
    };
    credential?: ICredential;
    createdAt: number;
    updatedAt: number;
    autoSignin?: boolean;
    expiry?: number;
}

// from https://github.com/WebOfTrust/signify-browser-extension/blob/d51ba75a3258a7a29267044235b915e1d0444075/src/config/types.ts
interface ISessionConfig {
    sessionOneTime: boolean;
}

// from https://github.com/WebOfTrust/signify-browser-extension/blob/d51ba75a3258a7a29267044235b915e1d0444075/src/config/types.ts
interface ICredential {
    issueeName: string;
    ancatc: string[];
    sad: { a: { i: string }; d: string };
    schema: {
        title: string;
        credentialType: string;
        description: string;
    };
    status: {
        et: string;
    };
    cesr?: string;
}
