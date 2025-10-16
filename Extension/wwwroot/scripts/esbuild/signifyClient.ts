/* eslint-disable @typescript-eslint/no-unused-vars */
/* eslint-disable no-unused-vars */

// This module provides a TypeScript wrapper around signify-ts for C# interop.
// Inspired by: https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/pages/background/services/signify.ts
// Source commit hash: d51ba75a3258a7a29267044235b915e1d0444075
//
// Note: This is a KERI-focused wrapper with no browser session management or alarm features.
// All functions return JSON strings for C# interop compatibility.

import type {
    EventResult,
    Operation,
    Contact,
    ContactInfo,
    CredentialData,
    IpexApplyArgs,
    IpexOfferArgs,
    IpexAgreeArgs,
    IpexGrantArgs,
    IpexAdmitArgs,
    CreateRegistryArgs,
    Serder
} from 'signify-ts';

// eslint-disable-next-line no-duplicate-imports
import {
    SignifyClient,
    Tier,
    ready
} from 'signify-ts';

// Re-export ready for libsodium initialization
export { ready };

// ===================== Type Definitions =====================

// Controller state interface (based on signify-ts ClientState)
interface ControllerState {
    i: string;      // Client AID Prefix
    k?: string[];   // Client AID Keys
    n?: string;     // Client AID Next Keys Digest
}

// Controller wrapper interface
interface Controller {
    state: ControllerState;
}

// Agent interface
interface Agent {
    i: string;      // Agent AID Prefix
    et?: string;    // Agent AID Type (e.g., 'dip' for delegated inception)
    di?: string;    // Agent AID Delegator
}

// Full client state returned by SignifyClient.state()
interface ClientState {
    agent: Agent | null;
    controller: Controller | null;
    ridx: number;
    pidx: number;
}

// Identifier interface (used by signify-ts)
export interface IIdentifier {
    name?: string;
    prefix: string;
}

// ===================== Module State =====================

let _client: SignifyClient | null = null;

// ===================== Helper Functions =====================

const objectToJson = (obj: object): string => JSON.stringify(obj);

const validateClient = (): void => {
    if (!_client) {
        throw new Error('signifyClient: SignifyClient not connected');
    }
};

/**
 * Disconnect and reset client state
 * Should be called on cancellation or timeout to ensure clean state
 */
export const disconnect = (): void => {
    console.debug('signifyClient: disconnect - Resetting client state');
    _client = null;
};

// ===================== Connection & Initialization =====================

/**
 * Boot and connect a SignifyClient instance
 * @param agentUrl - KERIA agent URL
 * @param bootUrl - Boot URL for initialization
 * @param passcode - 21-character passcode
 * @returns JSON string representation of connection result
 */
export const bootAndConnect = async (
    agentUrl: string,
    bootUrl: string,
    passcode: string
): Promise<string> => {
    _client = null;
    await ready();
    console.debug('signifyClient: bootAndConnect: creating client...');

    // TODO P2: Consider raising Tier for production use
    _client = new SignifyClient(agentUrl, passcode, Tier.low, bootUrl);

    try {
        console.debug('signifyClient: booting...');
        const bootedSignifyClient = await _client.boot();
        if (!bootedSignifyClient.ok) {
            throw new Error('Boot operation failed');
        }
        console.debug('signifyClient: connecting...');
        await _client.connect();
    } catch (error) {
        console.error('signifyClient: client could not boot then connect', error);
        _client = null;
        throw error;
    }

    const state = await getState();
    console.debug('signifyClient: bootAndConnect: connected');

    return objectToJson({ success: true, state: JSON.parse(state) });
};

/**
 * Connect to an existing SignifyClient (no boot)
 * @param agentUrl - KERIA agent URL
 * @param passcode - 21-character passcode
 * @returns JSON string representation of connection result
 */
export const connect = async (agentUrl: string, passcode: string): Promise<string> => {
    _client = null;
    await ready();
    console.debug('signifyClient: connect: creating client...');
    _client = new SignifyClient(agentUrl, passcode, Tier.low, '');

    try {
        await _client.connect();
        console.debug('signifyClient: client connected');
    } catch (error) {
        console.error('signifyClient: client could not connect', error);
        _client = null;
        throw error;
    }

    const state = await getState();
    console.debug('signifyClient: connect: connected');

    return objectToJson({ success: true, state: JSON.parse(state) });
};

/**
 * Get the current client state (controller and agent info)
 * @returns JSON string of ClientState
 */
export const getState = async (): Promise<string> => {
    validateClient();
    const state = await _client!.state();
    console.debug('signifyClient: getState - Client AID:', state.controller.state.i);
    console.debug('signifyClient: getState - Agent AID:', state.agent.i);
    return objectToJson(state);
};

// ===================== Identifier (AID) Operations =====================

/**
 * Create a new AID (identifier)
 * @param name - Alias for the identifier
 * @returns JSON string containing the identifier prefix
 */
export const createAID = async (name: string): Promise<string> => {
    try {
        validateClient();
        const res: EventResult = await _client!.identifiers().create(name);
        const op = await res.op();
        const id: string = op.response.i;
        console.debug('signifyClient: createAID - Created AID:', id);
        return objectToJson({ prefix: id, name });
    } catch (error) {
        console.error('signifyClient: createAID error:', error);
        throw error;
    }
};

/**
 * List all identifiers managed by this client
 * @returns JSON array of identifiers
 */
export const getAIDs = async (): Promise<string> => {
    validateClient();
    const managedIdentifiers = await _client!.identifiers().list();
    console.debug('signifyClient: getAIDs - Count:', managedIdentifiers.aids?.length || 0);
    return objectToJson(managedIdentifiers);
};

/**
 * Get a specific identifier by name (alias)
 * @param name - Alias of the identifier
 * @returns JSON string of the identifier
 */
export const getAID = async (name: string): Promise<string> => {
    try {
        validateClient();
        const managedIdentifier = await _client!.identifiers().get(name);
        console.debug('signifyClient: getAID - Name:', name, 'Prefix:', managedIdentifier.prefix);
        return objectToJson(managedIdentifier);
    } catch (error) {
        console.error('signifyClient: getAID error - Name:', name, error);
        throw error;
    }
};

/**
 * Get identifier name by prefix
 * @param prefix - AID prefix
 * @returns Name/alias of the identifier
 */
export const getNameByPrefix = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        // TODO P2: Consider using getIdentifierByPrefix and extracting name from JSON in C#
        // to avoid duplicate calls to _client.identifiers().get()
        const aid = await _client!.identifiers().get(prefix) as IIdentifier | undefined;
        if (!aid) {
            throw new Error(`Identifier with prefix ${prefix} not found`);
        }
        const name = aid.name ? aid.name : '';
        console.debug('signifyClient: getNameByPrefix - Prefix:', prefix, 'Name:', name);
        return name;
    } catch (error) {
        console.error('signifyClient: getNameByPrefix error - Prefix:', prefix, error);
        throw error;
    }
};

/**
 * Get full identifier object by prefix
 * @param prefix - AID prefix
 * @returns JSON string of IIdentifier object
 */
export const getIdentifierByPrefix = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const aid = await _client!.identifiers().get(prefix) as IIdentifier | undefined;
        if (!aid) {
            throw new Error(`Identifier with prefix ${prefix} not found`);
        }
        console.debug('signifyClient: getIdentifierByPrefix - Prefix:', prefix);
        return objectToJson(aid);
    } catch (error) {
        console.error('signifyClient: getIdentifierByPrefix error - Prefix:', prefix, error);
        throw error;
    }
};

// ===================== Credential (ACDC) Operations =====================

/**
 * List all credentials
 * @returns JSON array of credentials
 */
export const getCredentialsList = async (): Promise<string> => {
    try {
        validateClient();
        const credentials: any = await _client!.credentials().list();
        console.debug('signifyClient: getCredentialsList - Count:', credentials?.length || 0);
        return objectToJson(credentials);
    } catch (error) {
        console.error('signifyClient: getCredentialsList error:', error);
        throw error;
    }
};

/**
 * Get a specific credential by SAID
 * @param id - Credential SAID
 * @param includeCESR - Include CESR encoding
 * @returns JSON string of the credential (complex object)
 */
export const getCredential = async (
    id: string,
    includeCESR: boolean = false
): Promise<string> => {
    try {
        validateClient();
        const credential = await _client!.credentials().get(id, includeCESR);
        console.debug('signifyClient: getCredential - SAID:', id, 'IncludeCESR:', includeCESR);
        return objectToJson(credential);
    } catch (error) {
        console.error('signifyClient: getCredential error:', error);
        throw error;
    }
};

/**
 * Issue a new credential
 * @param name - Identifier name
 * @param argsJson - JSON string of CredentialData
 * @returns JSON string of issuance result
 */
export const credentialsIssue = async (name: string, argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as CredentialData;
        const result = await _client!.credentials().issue(name, args);
        console.debug('signifyClient: credentialsIssue - Name:', name);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: credentialsIssue error:', error);
        throw error;
    }
};

/**
 * Revoke a credential
 * @param name - Identifier name
 * @param said - Credential SAID
 * @param datetime - Optional datetime for revocation
 * @returns JSON string of revocation result
 */
export const credentialsRevoke = async (name: string, said: string, datetime?: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.credentials().revoke(name, said, datetime);
        console.debug('signifyClient: credentialsRevoke - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: credentialsRevoke error:', error);
        throw error;
    }
};

/**
 * Get credential state
 * @param ri - Registry identifier
 * @param said - Credential SAID
 * @returns JSON string of credential state
 */
export const credentialsState = async (ri: string, said: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.credentials().state(ri, said);
        console.debug('signifyClient: credentialsState - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: credentialsState error:', error);
        throw error;
    }
};

/**
 * Delete a credential
 * @param said - Credential SAID
 * @returns JSON string confirming deletion
 */
export const credentialsDelete = async (said: string): Promise<string> => {
    try {
        validateClient();
        await _client!.credentials().delete(said);
        const result = { success: true, message: `Credential ${said} deleted successfully` };
        console.debug('signifyClient: credentialsDelete - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: credentialsDelete error:', error);
        throw error;
    }
};

// ===================== Signed Headers =====================

/**
 * Get signed headers for HTTP request authentication
 * @param origin - Origin URL
 * @param url - Resource URL
 * @param method - HTTP method
 * @param headersDict - Initial headers object
 * @param aidName - Identifier name for signing
 * @returns JSON object of signed headers
 */
export const getSignedHeaders = async (
    origin: string,
    url: string,
    method: string,
    headersDict: { [key: string]: string },
    aidName: string
): Promise<{ [key: string]: string }> => {
    console.debug('signifyClient: getSignedHeaders - Origin:', origin, 'URL:', url, 'Method:', method, 'AID:', aidName);

    validateClient();

    try {
        const requestInit: RequestInit = {
            method,
            headers: headersDict
        };
        const signedRequest: Request = await _client!.createSignedRequest(aidName, url, requestInit);

        console.debug('signifyClient: getSignedHeaders - Request signed successfully');

        // Log headers for debugging
        if (signedRequest.headers) {
            signedRequest.headers.forEach((value, key) => {
                console.debug(`  ${key}: ${value}`);
            });
        }

        const jsonHeaders: { [key: string]: string } = {};
        if (signedRequest?.headers) {
            for (const pair of signedRequest.headers.entries()) {
                jsonHeaders[pair[0]] = String(pair[1]);
            }
        }

        return jsonHeaders;
    } catch (error) {
        console.error('signifyClient: getSignedHeaders error:', error);
        throw error;
    }
};

// ===================== IPEX Protocol Methods =====================

export const ipexApply = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as IpexApplyArgs;
        const [exn, sigs, end] = await _client!.ipex().apply(args);
        const result = { exn, sigs, end };
        console.debug('signifyClient: ipexApply - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexApply error:', error);
        throw error;
    }
};

export const ipexOffer = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as IpexOfferArgs;
        const [exn, sigs, end] = await _client!.ipex().offer(args);
        const result = { exn, sigs, end };
        console.debug('signifyClient: ipexOffer - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexOffer error:', error);
        throw error;
    }
};

export const ipexAgree = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as IpexAgreeArgs;
        const [exn, sigs, end] = await _client!.ipex().agree(args);
        const result = { exn, sigs, end };
        console.debug('signifyClient: ipexAgree - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexAgree error:', error);
        throw error;
    }
};

export const ipexGrant = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as IpexGrantArgs;
        const [exn, sigs, end] = await _client!.ipex().grant(args);
        const result = { exn, sigs, end };
        console.debug('signifyClient: ipexGrant - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexGrant error:', error);
        throw error;
    }
};

export const ipexAdmit = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as IpexAdmitArgs;
        const [exn, sigs, end] = await _client!.ipex().admit(args);
        const result = { exn, sigs, end };
        console.debug('signifyClient: ipexAdmit - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexAdmit error:', error);
        throw error;
    }
};

export const ipexSubmitApply = async (name: string, exnJson: string, sigsJson: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.ipex().submitApply(name, exn, sigs, recipients);
        console.debug('signifyClient: ipexSubmitApply - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexSubmitApply error:', error);
        throw error;
    }
};

export const ipexSubmitOffer = async (name: string, exnJson: string, sigsJson: string, atc: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.ipex().submitOffer(name, exn, sigs, atc, recipients);
        console.debug('signifyClient: ipexSubmitOffer - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexSubmitOffer error:', error);
        throw error;
    }
};

export const ipexSubmitAgree = async (name: string, exnJson: string, sigsJson: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.ipex().submitAgree(name, exn, sigs, recipients);
        console.debug('signifyClient: ipexSubmitAgree - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexSubmitAgree error:', error);
        throw error;
    }
};

export const ipexSubmitGrant = async (name: string, exnJson: string, sigsJson: string, atc: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.ipex().submitGrant(name, exn, sigs, atc, recipients);
        console.debug('signifyClient: ipexSubmitGrant - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexSubmitGrant error:', error);
        throw error;
    }
};

export const ipexSubmitAdmit = async (name: string, exnJson: string, sigsJson: string, atc: string, recipientsJson: string): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.ipex().submitAdmit(name, exn, sigs, atc, recipients);
        console.debug('signifyClient: ipexSubmitAdmit - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: ipexSubmitAdmit error:', error);
        throw error;
    }
};

// ===================== OOBI Operations =====================

export const oobiGet = async (name: string, role?: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.oobis().get(name, role);
        console.debug('signifyClient: oobiGet - Name:', name, 'Role:', role);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: oobiGet error:', error);
        throw error;
    }
};

export const oobiResolve = async (oobi: string, alias?: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.oobis().resolve(oobi, alias);
        console.debug('signifyClient: oobiResolve - Alias:', alias);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: oobiResolve error:', error);
        throw error;
    }
};

// ===================== Operations Management =====================

export const operationsGet = async <T = unknown>(name: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.operations().get<T>(name);
        console.debug('signifyClient: operationsGet - Name:', name);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: operationsGet error:', error);
        throw error;
    }
};

export const operationsList = async (type?: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.operations().list(type);
        console.debug('signifyClient: operationsList - Type:', type);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: operationsList error:', error);
        throw error;
    }
};

export const operationsDelete = async (name: string): Promise<string> => {
    try {
        validateClient();
        await _client!.operations().delete(name);
        const result = { success: true, message: `Operation ${name} deleted successfully` };
        console.debug('signifyClient: operationsDelete - Name:', name);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: operationsDelete error:', error);
        throw error;
    }
};

export const operationsWait = async <T = unknown>(
    operationJson: string,
    optionsJson?: string
): Promise<string> => {
    try {
        validateClient();
        const operation = JSON.parse(operationJson) as Operation<T>;
        const options = optionsJson ? JSON.parse(optionsJson) : undefined;
        const result = await _client!.operations().wait<T>(operation, options);
        console.debug('signifyClient: operationsWait - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: operationsWait error:', error);
        throw error;
    }
};

// ===================== Registry Management =====================

export const registriesList = async (name: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.registries().list(name);
        console.debug('signifyClient: registriesList - Name:', name);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: registriesList error:', error);
        throw error;
    }
};

export const registriesCreate = async (argsJson: string): Promise<string> => {
    try {
        validateClient();
        const args = JSON.parse(argsJson) as CreateRegistryArgs;
        const result = await _client!.registries().create(args);
        console.debug('signifyClient: registriesCreate - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: registriesCreate error:', error);
        throw error;
    }
};

export const registriesRename = async (name: string, registryName: string, newName: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.registries().rename(name, registryName, newName);
        console.debug('signifyClient: registriesRename - New name:', newName);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: registriesRename error:', error);
        throw error;
    }
};

// ===================== Contact Management =====================

export const contactsList = async (group?: string, filterField?: string, filterValue?: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.contacts().list(group, filterField, filterValue);
        console.debug('signifyClient: contactsList - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: contactsList error:', error);
        throw error;
    }
};

export const contactsGet = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.contacts().get(prefix);
        console.debug('signifyClient: contactsGet - Prefix:', prefix);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: contactsGet error:', error);
        throw error;
    }
};

export const contactsAdd = async (prefix: string, infoJson: string): Promise<string> => {
    try {
        validateClient();
        const info = JSON.parse(infoJson) as ContactInfo;
        const result = await _client!.contacts().add(prefix, info);
        console.debug('signifyClient: contactsAdd - Prefix:', prefix);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: contactsAdd error:', error);
        throw error;
    }
};

export const contactsUpdate = async (prefix: string, infoJson: string): Promise<string> => {
    try {
        validateClient();
        const info = JSON.parse(infoJson) as ContactInfo;
        const result = await _client!.contacts().update(prefix, info);
        console.debug('signifyClient: contactsUpdate - Prefix:', prefix);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: contactsUpdate error:', error);
        throw error;
    }
};

export const contactsDelete = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        await _client!.contacts().delete(prefix);
        const result = { success: true, message: `Contact ${prefix} deleted successfully` };
        console.debug('signifyClient: contactsDelete - Prefix:', prefix);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: contactsDelete error:', error);
        throw error;
    }
};

// ===================== Schema Operations =====================

export const schemasGet = async (said: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.schemas().get(said);
        console.debug('signifyClient: schemasGet - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: schemasGet error:', error);
        throw error;
    }
};

export const schemasList = async (): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.schemas().list();
        console.debug('signifyClient: schemasList - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: schemasList error:', error);
        throw error;
    }
};

// ===================== Notifications Operations =====================

export const notificationsList = async (start?: number, end?: number): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.notifications().list(start, end);
        console.debug('signifyClient: notificationsList - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: notificationsList error:', error);
        throw error;
    }
};

export const notificationsMark = async (said: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.notifications().mark(said);
        console.debug('signifyClient: notificationsMark - SAID:', said);
        return result; // Already a string
    } catch (error) {
        console.error('signifyClient: notificationsMark error:', error);
        throw error;
    }
};

export const notificationsDelete = async (said: string): Promise<string> => {
    try {
        validateClient();
        await _client!.notifications().delete(said);
        const result = { success: true, message: `Notification ${said} deleted successfully` };
        console.debug('signifyClient: notificationsDelete - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: notificationsDelete error:', error);
        throw error;
    }
};

// ===================== Escrows Operations =====================

/**
 * List replay messages from escrow
 * @param route - Optional route to filter replay messages
 * @returns JSON string of replay messages
 */
export const escrowsListReply = async (route?: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.escrows().listReply(route);
        console.debug('signifyClient: escrowsListReply - Route:', route);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: escrowsListReply error:', error);
        throw error;
    }
};

// ===================== Groups Operations =====================

/**
 * Get group request message by SAID
 * @param said - SAID of the exn message
 * @returns JSON string of the group request
 */
export const groupsGetRequest = async (said: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.groups().getRequest(said);
        console.debug('signifyClient: groupsGetRequest - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: groupsGetRequest error:', error);
        throw error;
    }
};

/**
 * Send multisig exn request to other group members
 * @param name - Human readable name of group AID
 * @param exnJson - JSON string of exn message
 * @param sigsJson - JSON string of signatures array
 * @param atc - Additional attachments from embedded events
 * @returns JSON string of send result
 */
export const groupsSendRequest = async (
    name: string,
    exnJson: string,
    sigsJson: string,
    atc: string
): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson);
        const sigs = JSON.parse(sigsJson) as string[];
        const result = await _client!.groups().sendRequest(name, exn, sigs, atc);
        console.debug('signifyClient: groupsSendRequest - Name:', name);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: groupsSendRequest error:', error);
        throw error;
    }
};

/**
 * Join multisig group using rotation event
 * @param name - Human readable name of group AID
 * @param rotJson - JSON string of rotation event
 * @param sigsJson - JSON string of signatures
 * @param gid - Group identifier prefix
 * @param smidsJson - JSON string array of signing member identifiers
 * @param rmidsJson - JSON string array of rotating member identifiers
 * @returns JSON string of join result
 */
export const groupsJoin = async (
    name: string,
    rotJson: string,
    sigsJson: string,
    gid: string,
    smidsJson: string,
    rmidsJson: string
): Promise<string> => {
    try {
        validateClient();
        const rot = JSON.parse(rotJson);
        const sigs = JSON.parse(sigsJson);
        const smids = JSON.parse(smidsJson) as string[];
        const rmids = JSON.parse(rmidsJson) as string[];
        const result = await _client!.groups().join(name, rot, sigs, gid, smids, rmids);
        console.debug('signifyClient: groupsJoin - Name:', name, 'GID:', gid);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: groupsJoin error:', error);
        throw error;
    }
};

// ===================== Exchanges Operations =====================

/**
 * Get exchange message by SAID
 * @param said - SAID of the exchange message
 * @returns JSON string of the exchange message
 */
export const exchangesGet = async (said: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.exchanges().get(said);
        console.debug('signifyClient: exchangesGet - SAID:', said);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: exchangesGet error:', error);
        throw error;
    }
};

/**
 * Send exchange message to recipients
 * @param name - Identifier name
 * @param topic - Message topic
 * @param senderJson - JSON string of sender HabState
 * @param route - Exchange route
 * @param payloadJson - JSON string of payload
 * @param embedsJson - JSON string of embedded data
 * @param recipientsJson - JSON string array of recipient identifiers
 * @returns JSON string of send result
 */
export const exchangesSend = async (
    name: string,
    topic: string,
    senderJson: string,
    route: string,
    payloadJson: string,
    embedsJson: string,
    recipientsJson: string
): Promise<string> => {
    try {
        validateClient();
        const sender = JSON.parse(senderJson);
        const payload = JSON.parse(payloadJson);
        const embeds = JSON.parse(embedsJson);
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.exchanges().send(name, topic, sender, route, payload, embeds, recipients);
        console.debug('signifyClient: exchangesSend - Name:', name, 'Topic:', topic);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: exchangesSend error:', error);
        throw error;
    }
};

/**
 * Send exchange message from pre-created events
 * @param name - Identifier name
 * @param topic - Message topic
 * @param exnJson - JSON string of Serder exchange message
 * @param sigsJson - JSON string of signatures array
 * @param atc - Additional attachments
 * @param recipientsJson - JSON string array of recipient identifiers
 * @returns JSON string of send result
 */
export const exchangesSendFromEvents = async (
    name: string,
    topic: string,
    exnJson: string,
    sigsJson: string,
    atc: string,
    recipientsJson: string
): Promise<string> => {
    try {
        validateClient();
        const exn = JSON.parse(exnJson) as Serder;
        const sigs = JSON.parse(sigsJson) as string[];
        const recipients = JSON.parse(recipientsJson) as string[];
        const result = await _client!.exchanges().sendFromEvents(name, topic, exn, sigs, atc, recipients);
        console.debug('signifyClient: exchangesSendFromEvents - Name:', name, 'Topic:', topic);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: exchangesSendFromEvents error:', error);
        throw error;
    }
};

// ===================== Delegations Operations =====================

/**
 * Approve delegation via interaction event
 * @param name - Name or alias of the identifier
 * @param dataJson - Optional JSON string of anchoring interaction event data
 * @returns JSON string of approval result with operation
 */
export const delegationsApprove = async (name: string, dataJson?: string): Promise<string> => {
    try {
        validateClient();
        const data = dataJson ? JSON.parse(dataJson) : undefined;
        const result = await _client!.delegations().approve(name, data);
        console.debug('signifyClient: delegationsApprove - Name:', name);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: delegationsApprove error:', error);
        throw error;
    }
};

// ===================== KeyEvents Operations =====================

/**
 * Get key events for an identifier
 * @param prefix - Identifier prefix
 * @returns JSON string of key events
 */
export const keyEventsGet = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.keyEvents().get(prefix);
        console.debug('signifyClient: keyEventsGet - Prefix:', prefix);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: keyEventsGet error:', error);
        throw error;
    }
};

// ===================== KeyStates Operations =====================

/**
 * Get key state for an identifier
 * @param prefix - Identifier prefix
 * @returns JSON string of key state
 */
export const keyStatesGet = async (prefix: string): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.keyStates().get(prefix);
        console.debug('signifyClient: keyStatesGet - Prefix:', prefix);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: keyStatesGet error:', error);
        throw error;
    }
};

/**
 * Get key states for multiple identifiers
 * @param prefixesJson - JSON string array of identifier prefixes
 * @returns JSON string of key states
 */
export const keyStatesList = async (prefixesJson: string): Promise<string> => {
    try {
        validateClient();
        const prefixes = JSON.parse(prefixesJson) as string[];
        const result = await _client!.keyStates().list(prefixes);
        console.debug('signifyClient: keyStatesList - Count:', prefixes.length);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: keyStatesList error:', error);
        throw error;
    }
};

/**
 * Query key state at specific sequence number or anchor
 * @param prefix - Identifier prefix
 * @param sn - Optional sequence number
 * @param anchorJson - Optional JSON string of anchor data
 * @returns JSON string of query operation
 */
export const keyStatesQuery = async (prefix: string, sn?: string, anchorJson?: string): Promise<string> => {
    try {
        validateClient();
        const anchor = anchorJson ? JSON.parse(anchorJson) : undefined;
        const result = await _client!.keyStates().query(prefix, sn, anchor);
        console.debug('signifyClient: keyStatesQuery - Prefix:', prefix, 'SN:', sn);
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: keyStatesQuery error:', error);
        throw error;
    }
};

// ===================== Config Operations =====================

/**
 * Get agent configuration
 * @returns JSON string of agent config
 */
export const configGet = async (): Promise<string> => {
    try {
        validateClient();
        const result = await _client!.config().get();
        console.debug('signifyClient: configGet - Success');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: configGet error:', error);
        throw error;
    }
};

// ===================== Challenges Operations =====================

/**
 * Get challenges resource
 * Note: Full API not documented in signify-ts 0.3.0-rc2 type definitions
 * TODO: Implement specific challenge methods when API is clarified
 */
export const challengesPlaceholder = async (): Promise<string> => {
    try {
        validateClient();
        // Challenges API methods need to be determined from signify-ts source
        const result = { message: 'Challenges API - methods not yet defined in signify-ts types' };
        console.debug('signifyClient: challengesPlaceholder - Not yet implemented');
        return objectToJson(result);
    } catch (error) {
        console.error('signifyClient: challengesPlaceholder error:', error);
        throw error;
    }
};
