/* eslint-disable @typescript-eslint/no-unused-vars */
/* eslint-disable no-unused-vars */
// Note the compilation of this .ts file is bundled with its dependencies.  See entry in package.json for its build.
// This Javascript-C# interop layer is paired with Signify_ts_shim.cs

// See the following for more inspiration:
// https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/pages/background/services/signify.ts

import type {
    EventResult,
    Operation,
    Contact,
    ContactInfo,
    CredentialData,
    CredentialSubject,
    CredentialFilter,
    CredentialState,
    IpexApplyArgs,
    IpexOfferArgs,
    IpexAgreeArgs,
    IpexGrantArgs,
    IpexAdmitArgs,
    CreateRegistryArgs,
    State as SignifyState,
    HabState,
    Challenge,
    Serder
} from 'signify-ts';

// eslint-disable-next-line no-duplicate-imports
import {
    SignifyClient,
    Tier,
    ready
} from 'signify-ts';

// Type definitions for SignifyClient.state() return value
// Based on https://github.com/WebOfTrust/signify-ts/blob/d48adc0db6d224bace531de2c2bb14f1058f076d/src/keri/app/clienting.ts#L19

// Controller state interface (also referred to as "client" in some contexts)
interface ControllerState {
    i: string;  // Client AID Prefix
    k?: string[];  // Client AID Keys
    n?: string;  // Client AID Next Keys Digest
}

// Controller wrapper interface
interface Controller {
    state: ControllerState;
}

// Agent interface
interface Agent {
    i: string;  // Agent AID Prefix
    et?: string;  // Agent AID Type (e.g., 'dip' for delegated inception)
    di?: string;  // Agent AID Delegator (should be the Client AID's prefix)
}

interface ClientState {
    agent: Agent | null;
    controller: Controller | null;
    ridx: number;
    pidx: number;
}

// export const PASSCODE_TIMEOUT = 5;

let _client: SignifyClient | null;

/**
 * Boot then Connect a SignifyClient instance
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
        console.debug('signify_ts_shim: booting...');
        const bootedSignifyClient = await _client.boot();
        if (!bootedSignifyClient.ok) {
            throw new Error();
        }
        console.debug('signify_ts_shim: connecting...');
        await _client.connect();
    } catch (error) {
        console.error('signify_ts_shim: client could not boot then connect', error);
        throw error;
    }

    // note that uncommenting the next line might expose the passkey
    // console.error('signify_ts_shim: client', {agent: _client.agent?.pre,controller: _client.controller.pre});
    const state = await getState();
    console.debug('signify_ts_shim: bootAndConnect: connected');
    // console.assert(state?.controller?.state?.i !== null, 'controller id is null'); // TODO P2 throw exception?

    return objectToJson(_client);
};

const objectToJson = (obj: object): string => JSON.stringify(obj);

const validateClient = (): Promise<SignifyClient> => new Promise((resolve, reject) => {
    if (!_client) {
        reject(new Error('signify_ts_shim: Client not connected'));
    } else {
        resolve(_client as SignifyClient);
    }
});

export const getState = async (): Promise<string> => {
    const c = await validateClient();
    // TODO P2 the following exposes the keys in the console log!
    console.log('signify_ts_shim: validateClient: ', c);
    const s = await c.state();
    console.log('getState Client AID Prefix: ', s.controller.state.i);
    console.log('getState Agent AID Prefix:   ', s.agent.i);
    // TODO P2 the following exposes the keys in the console log?
    console.log('signify_ts_shim: getState: ', s);
    return objectToJson(s);
};

export const connect = async (agentUrl: string, passcode: string): Promise<string> => {
    _client = null;
    await ready();
    // TODO P2 the following exposes the keys in the console log?
    console.log('signify_ts_shim: connect: creating client...');
    _client = new SignifyClient(agentUrl, passcode, Tier.low, '');

    try {
        await _client.connect();
        console.log('signify_ts_shim: client connected');
    } catch {
        console.log('signify_ts_shim: client could not connect');
    }

    const state = await getState();
    console.log('signify_ts_shim: connect: connected');
    // console.assert(state?.controller?.state?.i !== null, 'controller id is null'); // TODO P2 throw exception?

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

export const getNameByPrefix = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const aid = await getIdentifierByPrefix(prefix);
        const name = aid.name ? aid.name : '';
        return name;
    } catch (error) {
        console.error('signify_ts_shim: getPrefixByName: prefix, error:', prefix, error);
        throw error;
    }
};

export const getIdentifierByPrefix = async (prefix: string): Promise<IIdentifier> => {
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
};

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

// ===================== IPEX Protocol Methods =====================

export const ipexApply = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as IpexApplyArgs;
        const [exn, sigs, end] = await client.ipex().apply(args);
        const result = { exn, sigs, end };
        console.log('signify_ts_shim: ipexApply result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexApply error:', error);
        throw error;
    }
};

export const ipexOffer = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as IpexOfferArgs;
        const [exn, sigs, end] = await client.ipex().offer(args);
        const result = { exn, sigs, end };
        console.log('signify_ts_shim: ipexOffer result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexOffer error:', error);
        throw error;
    }
};

export const ipexAgree = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as IpexAgreeArgs;
        const [exn, sigs, end] = await client.ipex().agree(args);
        const result = { exn, sigs, end };
        console.log('signify_ts_shim: ipexAgree result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexAgree error:', error);
        throw error;
    }
};

export const ipexGrant = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as IpexGrantArgs;
        const [exn, sigs, end] = await client.ipex().grant(args);
        const result = { exn, sigs, end };
        console.log('signify_ts_shim: ipexGrant result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexGrant error:', error);
        throw error;
    }
};

export const ipexAdmit = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as IpexAdmitArgs;
        const [exn, sigs, end] = await client.ipex().admit(args);
        const result = { exn, sigs, end };
        console.log('signify_ts_shim: ipexAdmit result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexAdmit error:', error);
        throw error;
    }
};

export const ipexSubmitApply = async (name: string, exnJson: string, sigsJson: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await client.ipex().submitApply(name, exn, sigs, recipients);
        console.log('signify_ts_shim: ipexSubmitApply result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexSubmitApply error:', error);
        throw error;
    }
};

export const ipexSubmitOffer = async (name: string, exnJson: string, sigsJson: string, atc: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await client.ipex().submitOffer(name, exn, sigs, atc, recipients);
        console.log('signify_ts_shim: ipexSubmitOffer result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexSubmitOffer error:', error);
        throw error;
    }
};

export const ipexSubmitAgree = async (name: string, exnJson: string, sigsJson: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await client.ipex().submitAgree(name, exn, sigs, recipients);
        console.log('signify_ts_shim: ipexSubmitAgree result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexSubmitAgree error:', error);
        throw error;
    }
};

export const ipexSubmitGrant = async (name: string, exnJson: string, sigsJson: string, atc: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await client.ipex().submitGrant(name, exn, sigs, atc, recipients);
        console.log('signify_ts_shim: ipexSubmitGrant result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexSubmitGrant error:', error);
        throw error;
    }
};

export const ipexSubmitAdmit = async (name: string, exnJson: string, sigsJson: string, atc: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await client.ipex().submitAdmit(name, exn, sigs, atc, recipients);
        console.log('signify_ts_shim: ipexSubmitAdmit result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: ipexSubmitAdmit error:', error);
        throw error;
    }
};

// ===================== OOBI Operations =====================

export const oobiGet = async (name: string, role?: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.oobis().get(name, role);
        console.log('signify_ts_shim: oobiGet result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: oobiGet error:', error);
        throw error;
    }
};

export const oobiResolve = async (oobi: string, alias?: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.oobis().resolve(oobi, alias);
        console.log('signify_ts_shim: oobiResolve result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: oobiResolve error:', error);
        throw error;
    }
};

// ===================== Operations Management =====================

export const operationsGet = async <T = unknown>(name: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.operations().get<T>(name);
        console.log('signify_ts_shim: operationsGet result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: operationsGet error:', error);
        throw error;
    }
};

export const operationsList = async (type?: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.operations().list(type);
        console.log('signify_ts_shim: operationsList result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: operationsList error:', error);
        throw error;
    }
};

export const operationsDelete = async (name: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        await client.operations().delete(name);
        const result = { success: true, message: `Operation ${name} deleted successfully` };
        console.log('signify_ts_shim: operationsDelete result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: operationsDelete error:', error);
        throw error;
    }
};

export const operationsWait = async <T = unknown>(
    operationJson: string,
    optionsJson?: string
): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const operation = JSON.parse(operationJson) as Operation<T>;
        const options = optionsJson ? JSON.parse(optionsJson) : undefined;
        const result = await client.operations().wait<T>(operation, options);
        console.log('signify_ts_shim: operationsWait result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: operationsWait error:', error);
        throw error;
    }
};

// ===================== Registry Management =====================

export const registriesList = async (name: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.registries().list(name);
        console.log('signify_ts_shim: registriesList result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: registriesList error:', error);
        throw error;
    }
};

export const registriesCreate = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as CreateRegistryArgs;
        const result = await client.registries().create(args);
        console.log('signify_ts_shim: registriesCreate result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: registriesCreate error:', error);
        throw error;
    }
};

export const registriesRename = async (name: string, registryName: string, newName: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.registries().rename(name, registryName, newName);
        console.log('signify_ts_shim: registriesRename result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: registriesRename error:', error);
        throw error;
    }
};

// ===================== Contact Management =====================

export const contactsList = async (group?: string, filterField?: string, filterValue?: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.contacts().list(group, filterField, filterValue);
        console.log('signify_ts_shim: contactsList result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: contactsList error:', error);
        throw error;
    }
};

export const contactsGet = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.contacts().get(prefix);
        console.log('signify_ts_shim: contactsGet result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: contactsGet error:', error);
        throw error;
    }
};

export const contactsAdd = async (prefix: string, infoJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const info = JSON.parse(infoJson) as ContactInfo;
        const result = await client.contacts().add(prefix, info);
        console.log('signify_ts_shim: contactsAdd result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: contactsAdd error:', error);
        throw error;
    }
};

export const contactsUpdate = async (prefix: string, infoJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const info = JSON.parse(infoJson) as ContactInfo;
        const result = await client.contacts().update(prefix, info);
        console.log('signify_ts_shim: contactsUpdate result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: contactsUpdate error:', error);
        throw error;
    }
};

export const contactsDelete = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        await client.contacts().delete(prefix);
        const result = { success: true, message: `Contact ${prefix} deleted successfully` };
        console.log('signify_ts_shim: contactsDelete result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: contactsDelete error:', error);
        throw error;
    }
};

// ===================== Additional Credential Operations =====================

export const credentialsIssue = async (name: string, argsJson: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const args = JSON.parse(argsJson) as CredentialData;
        const result = await client.credentials().issue(name, args);
        console.log('signify_ts_shim: credentialsIssue result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: credentialsIssue error:', error);
        throw error;
    }
};

export const credentialsRevoke = async (name: string, said: string, datetime?: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.credentials().revoke(name, said, datetime);
        console.log('signify_ts_shim: credentialsRevoke result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: credentialsRevoke error:', error);
        throw error;
    }
};

export const credentialsState = async (ri: string, said: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.credentials().state(ri, said);
        console.log('signify_ts_shim: credentialsState result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: credentialsState error:', error);
        throw error;
    }
};

export const credentialsDelete = async (said: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        await client.credentials().delete(said);
        const result = { success: true, message: `Credential ${said} deleted successfully` };
        console.log('signify_ts_shim: credentialsDelete result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: credentialsDelete error:', error);
        throw error;
    }
};

// ===================== Schemas Operations =====================

export const schemasGet = async (said: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.schemas().get(said);
        console.log('signify_ts_shim: schemasGet result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: schemasGet error:', error);
        throw error;
    }
};

export const schemasList = async (): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.schemas().list();
        console.log('signify_ts_shim: schemasList result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: schemasList error:', error);
        throw error;
    }
};

// ===================== Notifications Operations =====================

export const notificationsList = async (start?: number, end?: number): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.notifications().list(start, end);
        console.log('signify_ts_shim: notificationsList result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: notificationsList error:', error);
        throw error;
    }
};

export const notificationsMark = async (said: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        const result = await client.notifications().mark(said);
        console.log('signify_ts_shim: notificationsMark result:', result);
        return result; // result is already a string
    } catch (error) {
        console.error('signify_ts_shim: notificationsMark error:', error);
        throw error;
    }
};

export const notificationsDelete = async (said: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client as SignifyClient;
        await client.notifications().delete(said);
        const result = { success: true, message: `Notification ${said} deleted successfully` };
        console.log('signify_ts_shim: notificationsDelete result:', result);
        return objectToJson(result);
    } catch (error) {
        console.error('signify_ts_shim: notificationsDelete error:', error);
        throw error;
    }
};

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
