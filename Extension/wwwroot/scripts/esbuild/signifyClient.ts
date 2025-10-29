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

const validateClient = async (): Promise<boolean> => {
    if (!_client || !_client.agent) {
        console.log(`signifyClient: validateClient - SignifyClient not connected`);
        // if _client is expected to be connected but not (usually because backgroundWorker hybernated and restarted), then reconnect to KERIA)


        // TODO P0: temp
        const agentUrl = "http://localhost:3901"; // TODO P1: Store and retrieve actual agentUrl and passcode securely"
        const passcode = "D1zFlTuOGpECDQzOMO8vz";

        // TODO P1: wrap in try/catch
        // TODO P0: don't log state
        const status = await connect(agentUrl, passcode);
        console.log(`signifyClient: validateClient - Reconnected to SignifyClient - status: ${status}`);
        if (status === null) {
            return false;
        }
        return true;
    } else {
        console.log(`signifyClient: validateClient: already connected`);
        return true;
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

/**
 * Unified wrapper for client operations with validation, error handling, and logging
 * @param operationName - Name of the operation for logging (e.g., "createAID")
 * @param operation - Async function that performs the operation
 * @param logParams - Optional parameters to log
 * @returns JSON string of operation result
 */
const withClientOperation = async (
    operationName: string,
    operation: () => Promise<unknown>,
    logParams?: Record<string, unknown>
): Promise<string> => {
    try {
        let isValidated = await validateClient();
        const result = await operation();

        // Build log message
        const paramString = logParams
            ? ' - ' + Object.entries(logParams).map(([k, v]) => `${k}: ${v}`).join(', ')
            : '';
        console.debug(`signifyClient: ${operationName}${paramString}`);

        return objectToJson(result as object);
    } catch (error) {
        const paramString = logParams
            ? ' - ' + Object.entries(logParams).map(([k, v]) => `${k}: ${v}`).join(', ')
            : '';
        console.error(`signifyClient: ${operationName} error${paramString}`, error);
        throw error;
    }
};

/**
 * Factory for creating IPEX protocol methods (apply, offer, agree, grant, admit)
 * @param methodName - Name of the IPEX method
 * @param ipexFunction - The signify-ts ipex function to call
 * @returns Wrapped async function
 */
const createIpexMethod = <TArgs>(
    methodName: string,
    ipexFunction: (args: TArgs) => Promise<[any, any, any]>
) => {
    return async (argsJson: string): Promise<string> => {
        return withClientOperation(
            `ipex${methodName}`,
            async () => {
                const args = JSON.parse(argsJson) as TArgs;
                const [exn, sigs, end] = await ipexFunction(args);
                return { exn, sigs, end };
            }
        );
    };
};

/**
 * Factory for creating IPEX submit methods (submitApply, submitOffer, etc.)
 * @param methodName - Name of the submit method
 * @param submitFunction - The signify-ts ipex submit function to call
 * @param hasAtc - Whether the method includes an 'atc' parameter
 * @returns Wrapped async function
 */
const createIpexSubmitMethod = (
    methodName: string,
    submitFunction: (name: string, exn: Serder, sigs: string[], ...args: any[]) => Promise<any>,
    hasAtc: boolean = false
) => {
    return async (
        name: string,
        exnJson: string,
        sigsJson: string,
        atcOrRecipientsJson: string,
        recipientsJson?: string
    ): Promise<string> => {
        return withClientOperation(
            `ipex${methodName}`,
            async () => {
                const exn = JSON.parse(exnJson) as Serder;
                const sigs = JSON.parse(sigsJson) as string[];

                if (hasAtc && recipientsJson) {
                    const atc = atcOrRecipientsJson;
                    const recipients = JSON.parse(recipientsJson) as string[];
                    return await submitFunction(name, exn, sigs, atc, recipients);
                } else {
                    const recipients = JSON.parse(atcOrRecipientsJson) as string[];
                    return await submitFunction(name, exn, sigs, recipients);
                }
            },
            { Name: name }
        );
    };
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
        // TODO P1: if _bootedSignifyClient contains updated client info, use it and no need to connect again.
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
    console.debug('signifyClient: connect: recreating client...');
    _client = null;
    
    console.debug('signifyClient: connect: recreating client...');
    
    // TODO P2: Consider raising Tier for production use
    _client = null;
    await ready();
    _client = new SignifyClient(agentUrl, passcode, Tier.low, ''); 
    console.debug('signifyClient: connect: created client...');
    
    console.debug('signifyClient: connect: ready to connect');
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
    let isValidated = await validateClient();
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
    return withClientOperation(
        'createAID',
        async () => {
            const res: EventResult = await _client!.identifiers().create(name);
            const op = await res.op();
            const id: string = op.response.i;
            return { prefix: id, name };
        },
        { Name: name }
    );
};

/**
 * List all identifiers managed by this client
 * @returns JSON array of identifiers
 */
export const getAIDs = async (): Promise<string> => {
    return withClientOperation(
        'getAIDs',
        async () => {
            const managedIdentifiers = await _client!.identifiers().list();
            return managedIdentifiers;
        }
    );
};

/**
 * Get a specific identifier by name (alias)
 * @param name - Alias of the identifier
 * @returns JSON string of the identifier
 */
export const getAID = async (name: string): Promise<string> => {
    return withClientOperation(
        'getAID',
        () => _client!.identifiers().get(name),
        { Name: name }
    );
};

/**
 * Get identifier name by prefix
 * @param prefix - AID prefix
 * @returns Name/alias of the identifier
 */
export const getNameByPrefix = async (prefix: string): Promise<string> => {
    // TODO P2: Consider using getIdentifierByPrefix and extracting name from JSON in C#
    // to avoid duplicate calls to _client.identifiers().get()
    const result = await withClientOperation(
        'getNameByPrefix',
        async () => {
            const aid = await _client!.identifiers().get(prefix) as IIdentifier | undefined;
            if (!aid) {
                throw new Error(`Identifier with prefix ${prefix} not found`);
            }
            return aid.name ? aid.name : '';
        },
        { Prefix: prefix }
    );
    // Return the raw string, not JSON-encoded
    return JSON.parse(result) as string;
};

/**
 * Get full identifier object by prefix
 * @param prefix - AID prefix
 * @returns JSON string of IIdentifier object
 */
export const getIdentifierByPrefix = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'getIdentifierByPrefix',
        async () => {
            const aid = await _client!.identifiers().get(prefix) as IIdentifier | undefined;
            if (!aid) {
                throw new Error(`Identifier with prefix ${prefix} not found`);
            }
            return aid;
        },
        { Prefix: prefix }
    );
};

/**
 * Rename an identifier (update its alias/name)
 * @param currentName - Current name/alias of the identifier
 * @param newName - New name/alias for the identifier
 * @returns JSON string of update result
 */
export const renameAID = async (currentName: string, newName: string): Promise<string> => {
    return withClientOperation(
        'renameAID',
        () => _client!.identifiers().update(currentName, { name: newName }),
        { 'Old name': currentName, 'New name': newName }
    );
};

// ===================== Credential (ACDC) Operations =====================

/**
 * List all credentials
 * @returns JSON array of credentials
 */
export const getCredentialsList = async (): Promise<string> => {
    return withClientOperation(
        'getCredentialsList',
        async () => {
            const credentials: any = await _client!.credentials().list();
            return credentials;
        },
        { Count: 'retrieved' }
    );
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
    return withClientOperation(
        'getCredential',
        () => _client!.credentials().get(id, includeCESR as any),
        { SAID: id, IncludeCESR: includeCESR }
    );
};

/**
 * Issue a new credential
 * @param name - Identifier name
 * @param argsJson - JSON string of CredentialData
 * @returns JSON string of issuance result
 */
export const credentialsIssue = async (name: string, argsJson: string): Promise<string> => {
    return withClientOperation(
        'credentialsIssue',
        async () => {
            const args = JSON.parse(argsJson) as CredentialData;
            return await _client!.credentials().issue(name, args);
        },
        { Name: name }
    );
};

/**
 * Revoke a credential
 * @param name - Identifier name
 * @param said - Credential SAID
 * @param datetime - Optional datetime for revocation
 * @returns JSON string of revocation result
 */
export const credentialsRevoke = async (name: string, said: string, datetime?: string): Promise<string> => {
    return withClientOperation(
        'credentialsRevoke',
        () => _client!.credentials().revoke(name, said, datetime),
        { SAID: said }
    );
};

/**
 * Get credential state
 * @param ri - Registry identifier
 * @param said - Credential SAID
 * @returns JSON string of credential state
 */
export const credentialsState = async (ri: string, said: string): Promise<string> => {
    return withClientOperation(
        'credentialsState',
        () => _client!.credentials().state(ri, said),
        { SAID: said }
    );
};

/**
 * Delete a credential
 * @param said - Credential SAID
 * @returns JSON string confirming deletion
 */
export const credentialsDelete = async (said: string): Promise<string> => {
    return withClientOperation(
        'credentialsDelete',
        async () => {
            await _client!.credentials().delete(said);
            return { success: true, message: `Credential ${said} deleted successfully` };
        },
        { SAID: said }
    );
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
    let isValidated = await validateClient();
    if (!isValidated) {
        throw new Error('signifyClient: getSignedHeaders - SignifyClient not connected');
    }
    console.debug('signifyClient: getSignedHeaders - Origin:', origin, 'URL:', url, 'Method:', method, 'AID:', aidName);

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

export const ipexApply = createIpexMethod<IpexApplyArgs>(
    'Apply',
    (args) => _client!.ipex().apply(args)
);

export const ipexOffer = createIpexMethod<IpexOfferArgs>(
    'Offer',
    (args) => _client!.ipex().offer(args)
);

export const ipexAgree = createIpexMethod<IpexAgreeArgs>(
    'Agree',
    (args) => _client!.ipex().agree(args)
);

export const ipexGrant = createIpexMethod<IpexGrantArgs>(
    'Grant',
    (args) => _client!.ipex().grant(args)
);

export const ipexAdmit = createIpexMethod<IpexAdmitArgs>(
    'Admit',
    (args) => _client!.ipex().admit(args)
);

export const ipexSubmitApply = createIpexSubmitMethod(
    'SubmitApply',
    (name, exn, sigs, recipients) => _client!.ipex().submitApply(name, exn, sigs, recipients),
    false
);

export const ipexSubmitOffer = createIpexSubmitMethod(
    'SubmitOffer',
    (name, exn, sigs, atc, recipients) => _client!.ipex().submitOffer(name, exn, sigs, atc, recipients),
    true
);

export const ipexSubmitAgree = createIpexSubmitMethod(
    'SubmitAgree',
    (name, exn, sigs, recipients) => _client!.ipex().submitAgree(name, exn, sigs, recipients),
    false
);

export const ipexSubmitGrant = createIpexSubmitMethod(
    'SubmitGrant',
    (name, exn, sigs, atc, recipients) => _client!.ipex().submitGrant(name, exn, sigs, atc, recipients),
    true
);

export const ipexSubmitAdmit = createIpexSubmitMethod(
    'SubmitAdmit',
    (name, exn, sigs, atc, recipients) => _client!.ipex().submitAdmit(name, exn, sigs, atc, recipients),
    true
);

// ===================== OOBI Operations =====================

export const oobiGet = async (name: string, role?: string): Promise<string> => {
    return withClientOperation(
        'oobiGet',
        () => _client!.oobis().get(name, role),
        { Name: name, Role: role }
    );
};

export const oobiResolve = async (oobi: string, alias?: string): Promise<string> => {
    return withClientOperation(
        'oobiResolve',
        () => _client!.oobis().resolve(oobi, alias),
        { Alias: alias }
    );
};

// ===================== Operations Management =====================

export const operationsGet = async <T = unknown>(name: string): Promise<string> => {
    return withClientOperation(
        'operationsGet',
        () => _client!.operations().get<T>(name),
        { Name: name }
    );
};

export const operationsList = async (type?: string): Promise<string> => {
    return withClientOperation(
        'operationsList',
        () => _client!.operations().list(type),
        { Type: type }
    );
};

export const operationsDelete = async (name: string): Promise<string> => {
    return withClientOperation(
        'operationsDelete',
        async () => {
            await _client!.operations().delete(name);
            return { success: true, message: `Operation ${name} deleted successfully` };
        },
        { Name: name }
    );
};

export const operationsWait = async <T = unknown>(
    operationJson: string,
    optionsJson?: string
): Promise<string> => {
    return withClientOperation(
        'operationsWait',
        async () => {
            const operation = JSON.parse(operationJson) as Operation<T>;
            const options = optionsJson ? JSON.parse(optionsJson) : undefined;
            return await _client!.operations().wait<T>(operation, options);
        }
    );
};

// ===================== Registry Management =====================

export const registriesList = async (name: string): Promise<string> => {
    return withClientOperation(
        'registriesList',
        () => _client!.registries().list(name),
        { Name: name }
    );
};

export const registriesCreate = async (argsJson: string): Promise<string> => {
    return withClientOperation(
        'registriesCreate',
        async () => {
            const args = JSON.parse(argsJson) as CreateRegistryArgs;
            return await _client!.registries().create(args);
        }
    );
};

export const registriesRename = async (name: string, registryName: string, newName: string): Promise<string> => {
    return withClientOperation(
        'registriesRename',
        () => _client!.registries().rename(name, registryName, newName),
        { 'New name': newName }
    );
};

// ===================== Contact Management =====================

export const contactsList = async (group?: string, filterField?: string, filterValue?: string): Promise<string> => {
    return withClientOperation(
        'contactsList',
        () => _client!.contacts().list(group, filterField, filterValue)
    );
};

export const contactsGet = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'contactsGet',
        () => _client!.contacts().get(prefix),
        { Prefix: prefix }
    );
};

export const contactsAdd = async (prefix: string, infoJson: string): Promise<string> => {
    return withClientOperation(
        'contactsAdd',
        async () => {
            const info = JSON.parse(infoJson) as ContactInfo;
            return await _client!.contacts().add(prefix, info);
        },
        { Prefix: prefix }
    );
};

export const contactsUpdate = async (prefix: string, infoJson: string): Promise<string> => {
    return withClientOperation(
        'contactsUpdate',
        async () => {
            const info = JSON.parse(infoJson) as ContactInfo;
            return await _client!.contacts().update(prefix, info);
        },
        { Prefix: prefix }
    );
};

export const contactsDelete = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'contactsDelete',
        async () => {
            await _client!.contacts().delete(prefix);
            return { success: true, message: `Contact ${prefix} deleted successfully` };
        },
        { Prefix: prefix }
    );
};

// ===================== Schema Operations =====================

export const schemasGet = async (said: string): Promise<string> => {
    return withClientOperation(
        'schemasGet',
        () => _client!.schemas().get(said),
        { SAID: said }
    );
};

export const schemasList = async (): Promise<string> => {
    return withClientOperation(
        'schemasList',
        () => _client!.schemas().list()
    );
};

// ===================== Notifications Operations =====================

export const notificationsList = async (start?: number, end?: number): Promise<string> => {
    return withClientOperation(
        'notificationsList',
        () => _client!.notifications().list(start, end)
    );
};

export const notificationsMark = async (said: string): Promise<string> => {
    let isValidated = await validateClient();
    const result = await _client!.notifications().mark(said);
    console.debug('signifyClient: notificationsMark - SAID:', said);
    return result; // Already a string
};

export const notificationsDelete = async (said: string): Promise<string> => {
    return withClientOperation(
        'notificationsDelete',
        async () => {
            let isValidated = await validateClient();
            await _client!.notifications().delete(said);
            return { success: true, message: `Notification ${said} deleted successfully` };
        },
        { SAID: said }
    );
};

// ===================== Escrows Operations =====================

/**
 * List replay messages from escrow
 * @param route - Optional route to filter replay messages
 * @returns JSON string of replay messages
 */
export const escrowsListReply = async (route?: string): Promise<string> => {
    return withClientOperation(
        'escrowsListReply',
        () => _client!.escrows().listReply(route),
        { Route: route }
    );
};

// ===================== Groups Operations =====================

/**
 * Get group request message by SAID
 * @param said - SAID of the exn message
 * @returns JSON string of the group request
 */
export const groupsGetRequest = async (said: string): Promise<string> => {
    return withClientOperation(
        'groupsGetRequest',
        () => _client!.groups().getRequest(said),
        { SAID: said }
    );
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
    return withClientOperation(
        'groupsSendRequest',
        async () => {
            const exn = JSON.parse(exnJson);
            const sigs = JSON.parse(sigsJson) as string[];
            return await _client!.groups().sendRequest(name, exn, sigs, atc);
        },
        { Name: name }
    );
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
    return withClientOperation(
        'groupsJoin',
        async () => {
            const rot = JSON.parse(rotJson);
            const sigs = JSON.parse(sigsJson);
            const smids = JSON.parse(smidsJson) as string[];
            const rmids = JSON.parse(rmidsJson) as string[];
            return await _client!.groups().join(name, rot, sigs, gid, smids, rmids);
        },
        { Name: name, GID: gid }
    );
};

// ===================== Exchanges Operations =====================

/**
 * Get exchange message by SAID
 * @param said - SAID of the exchange message
 * @returns JSON string of the exchange message
 */
export const exchangesGet = async (said: string): Promise<string> => {
    return withClientOperation(
        'exchangesGet',
        () => _client!.exchanges().get(said),
        { SAID: said }
    );
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
    return withClientOperation(
        'exchangesSend',
        async () => {
            const sender = JSON.parse(senderJson);
            const payload = JSON.parse(payloadJson);
            const embeds = JSON.parse(embedsJson);
            const recipients = JSON.parse(recipientsJson) as string[];
            return await _client!.exchanges().send(name, topic, sender, route, payload, embeds, recipients);
        },
        { Name: name, Topic: topic }
    );
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
    return withClientOperation(
        'exchangesSendFromEvents',
        async () => {
            const exn = JSON.parse(exnJson) as Serder;
            const sigs = JSON.parse(sigsJson) as string[];
            const recipients = JSON.parse(recipientsJson) as string[];
            return await _client!.exchanges().sendFromEvents(name, topic, exn, sigs, atc, recipients);
        },
        { Name: name, Topic: topic }
    );
};

// ===================== Delegations Operations =====================

/**
 * Approve delegation via interaction event
 * @param name - Name or alias of the identifier
 * @param dataJson - Optional JSON string of anchoring interaction event data
 * @returns JSON string of approval result with operation
 */
export const delegationsApprove = async (name: string, dataJson?: string): Promise<string> => {
    return withClientOperation(
        'delegationsApprove',
        async () => {
            const data = dataJson ? JSON.parse(dataJson) : undefined;
            return await _client!.delegations().approve(name, data);
        },
        { Name: name }
    );
};

// ===================== KeyEvents Operations =====================

/**
 * Get key events for an identifier
 * @param prefix - Identifier prefix
 * @returns JSON string of key events
 */
export const keyEventsGet = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'keyEventsGet',
        () => _client!.keyEvents().get(prefix),
        { Prefix: prefix }
    );
};

// ===================== KeyStates Operations =====================

/**
 * Get key state for an identifier
 * @param prefix - Identifier prefix
 * @returns JSON string of key state
 */
export const keyStatesGet = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'keyStatesGet',
        () => _client!.keyStates().get(prefix),
        { Prefix: prefix }
    );
};

/**
 * Get key states for multiple identifiers
 * @param prefixesJson - JSON string array of identifier prefixes
 * @returns JSON string of key states
 */
export const keyStatesList = async (prefixesJson: string): Promise<string> => {
    return withClientOperation(
        'keyStatesList',
        async () => {
            const prefixes = JSON.parse(prefixesJson) as string[];
            return await _client!.keyStates().list(prefixes);
        }
    );
};

/**
 * Query key state at specific sequence number or anchor
 * @param prefix - Identifier prefix
 * @param sn - Optional sequence number
 * @param anchorJson - Optional JSON string of anchor data
 * @returns JSON string of query operation
 */
export const keyStatesQuery = async (prefix: string, sn?: string, anchorJson?: string): Promise<string> => {
    return withClientOperation(
        'keyStatesQuery',
        async () => {
            const anchor = anchorJson ? JSON.parse(anchorJson) : undefined;
            return await _client!.keyStates().query(prefix, sn, anchor);
        },
        { Prefix: prefix, SN: sn }
    );
};

// ===================== Config Operations =====================

/**
 * Get agent configuration
 * @returns JSON string of agent config
 */
export const configGet = async (): Promise<string> => {
    return withClientOperation(
        'configGet',
        () => _client!.config().get()
    );
};

// ===================== Challenges Operations =====================

/**
 * Get challenges resource
 * Note: Full API not documented in signify-ts 0.3.0-rc2 type definitions
 * TODO: Implement specific challenge methods when API is clarified
 */
export const challengesPlaceholder = async (): Promise<string> => {
    return withClientOperation(
        'challengesPlaceholder',
        async () => {
            // Challenges API methods need to be determined from signify-ts source
            return { message: 'Challenges API - methods not yet defined in signify-ts types' };
        }
    );
};
