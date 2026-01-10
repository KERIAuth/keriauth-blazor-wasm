/* eslint-disable @typescript-eslint/no-unused-vars */
/* eslint-disable no-unused-vars */

// This module provides a TypeScript wrapper around signify-ts for C# interop.
// Inspired by: https://github.com/WebOfTrust/signify-browser-extension/blob/main/src/pages/background/services/signify.ts
// Source commit hash: d51ba75a3258a7a29267044235b915e1d0444075
//
// Note: This is a KERI-focused wrapper with no browser session management or alarm features.
// All functions return JSON strings for C# interop compatibility.

import type { KeriaConnectConfig, PasscodeModel } from '@keriauth/types';
import { StorageKeys } from '@keriauth/types';
import type {
    EventResult,
    Operation,
    Contact,
    ContactInfo,
    CredentialData,
    CredentialResult,
    CredentialFilter,
    CredentialState,
    IssueCredentialResult,
    RevokeCredentialResult,
    IpexApplyArgs,
    IpexOfferArgs,
    IpexAgreeArgs,
    IpexGrantArgs,
    IpexAdmitArgs,
    CreateRegistryArgs,
    Registry,
    RegistryResult,
    Schema,
    Serder,
    HabState,
    KeyState,
    Challenge,
    AgentConfig,
    Dict
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

// Note: Most types are now imported directly from signify-ts.
// The following types extend or supplement signify-ts types for C# interop.

// Re-export HabState as the primary identifier type
// HabState from signify-ts includes: name, prefix, transferable, state (KeyState), windexes, icp_dt
export type { HabState, KeyState, Contact, ContactInfo, CredentialResult, CredentialState, Schema, Registry, Challenge, AgentConfig };

// ===================== Module State =====================

let _client: SignifyClient | null = null;

// ===================== Helper Functions =====================

const objectToJson = (obj: object): string => JSON.stringify(obj);

export const test = async (): Promise<string> => {
    return 'signifyClient: test - module is working';
};

const validateClient = async (): Promise<SignifyClient> => {
    if (!_client || !_client.agent) {
        // Client not connected - reconnect (usually because backgroundWorker hibernated and restarted)
        const configResult = await chrome.storage.local.get(StorageKeys.KeriaConnectConfig);
        const config = configResult?.[StorageKeys.KeriaConnectConfig] as KeriaConnectConfig | undefined;
        const agentUrl = config?.AdminUrl;

        const passcodeResult = await chrome.storage.session.get(StorageKeys.PasscodeModel);
        const passcodeModel = passcodeResult?.[StorageKeys.PasscodeModel] as PasscodeModel | undefined;
        const passcode = passcodeModel?.Passcode;

        if (!agentUrl || agentUrl === '' || !passcode || passcode === '') {
            return Promise.reject(new Error('signifyClient: validateClient - Missing agentUrl or passcode'));
        }

        // TODO P2 wrap in try-catch
        await connect(agentUrl, passcode);
        if (!_client) {
            throw new Error('signifyClient: validateClient - Failed to reconnect SignifyClient');
        }
        return _client;
    }
    return _client;
};

/**
 * Disconnect and reset client state
 * Should be called on cancellation or timeout to ensure clean state
 */
export const disconnect = (): void => {
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
    operation: (client: SignifyClient) => Promise<unknown>,
    logParams?: Record<string, unknown>
): Promise<string> => {
    try {
        const client = await validateClient();
        const result = await operation(client);

        // Build log message
        const paramString = logParams
            ? ` - ${Object.entries(logParams).map(([k, v]) => `${k}: ${v}`).join(', ')}`
            : '';
        console.debug(`signifyClient: ${operationName}${paramString}`);

        return objectToJson(result as object);
    } catch (error) {
        const paramString = logParams
            ? ` - ${Object.entries(logParams).map(([k, v]) => `${k}: ${v}`).join(', ')}`
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
    ipexFunction: (client: SignifyClient) => (args: TArgs) => Promise<[any, any, any]>
) => {
    return async (argsJson: string): Promise<string> => {
        return withClientOperation(
            `ipex${methodName}`,
            async (client) => {
                const args = JSON.parse(argsJson) as TArgs;
                const [exn, sigs, end] = await ipexFunction(client)(args);
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
    submitFunction: (client: SignifyClient) => (name: string, exn: Serder, sigs: string[], ...args: any[]) => Promise<any>,
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
            async (client) => {
                const exn = JSON.parse(exnJson) as Serder;
                const sigs = JSON.parse(sigsJson) as string[];

                if (hasAtc && recipientsJson) {
                    const atc = atcOrRecipientsJson;
                    const recipients = JSON.parse(recipientsJson) as string[];
                    return await submitFunction(client)(name, exn, sigs, atc, recipients);
                } else {
                    const recipients = JSON.parse(atcOrRecipientsJson) as string[];
                    return await submitFunction(client)(name, exn, sigs, recipients);
                }
            },
            { Name: name }
        );
    };
};

// ===================== Connection & Initialization =====================

/**
 * Helper to sleep for a specified duration
 */
const sleep = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms));

/**
 * Helper to wait for agent to be ready to accept authenticated requests.
 * This verifies the agent is fully operational by making an authenticated request.
 * @param client - SignifyClient instance
 * @param maxRetries - Maximum number of retry attempts
 * @param initialDelayMs - Initial delay between retries in milliseconds
 */
const waitForAgentReady = async (
    client: SignifyClient,
    maxRetries: number = 5,
    initialDelayMs: number = 500
): Promise<void> => {
    let delayMs = initialDelayMs;

    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            // Try to list identifiers as a readiness check
            // This uses authenticated requests which will fail if agent isn't ready
            await client.identifiers().list();
            return;
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : String(error);
            if (attempt === maxRetries) {
                throw new Error(`Agent not ready after ${maxRetries} attempts. Last error: ${errorMessage}`);
            }
            await sleep(delayMs);
            delayMs = Math.min(delayMs * 2, 10000);
        }
    }
};

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

    // TODO P2: Consider raising Tier for production use
    _client = new SignifyClient(agentUrl, passcode, Tier.low, bootUrl);

    try {
        // Boot the agent
        const bootResponse = await _client.boot().catch((e: Error) => {
            console.error('signifyClient: boot error', e);
            if (e.message === 'Failed to fetch') {
                throw new Error('Failed to boot signify client due to network connectivity', { cause: e });
            }
            throw new Error('Failed to boot signify client', { cause: e });
        });

        // Accept 409 (Conflict) as valid - indicates agent was already booted
        if (!bootResponse.ok && bootResponse.status !== 409) {
            const bootError = await bootResponse.text().catch(() => 'Unknown boot error');
            console.warn(`signifyClient: Unexpected boot status ${bootResponse.status}: ${bootError}`);
            throw new Error(`Failed to boot signify client: status ${bootResponse.status}`);
        }

        // Connect - creates controller/agent objects and calls approveDelegation
        await connectSignifyClient();

        // Verify the agent is ready for authenticated requests
        await waitForAgentReady(_client, 10, 2000);

    } catch (error) {
        console.error('signifyClient: bootAndConnect failed', error);
        _client = null;
        throw error;
    }

    const state = await getState();
    return objectToJson({ success: true, state: JSON.parse(state) });
};

/**
 * Internal helper to connect the signify client with proper error handling
 */
const connectSignifyClient = async (): Promise<void> => {
    if (!_client) {
        throw new Error('SignifyClient not initialized');
    }

    await _client.connect().catch((error: unknown) => {
        if (!(error instanceof Error)) {
            throw error;
        }

        // Check for network errors
        if (error.message === 'Failed to fetch') {
            throw new Error('Failed to connect signify client due to network connectivity', { cause: error });
        }

        // Check for 404 - agent not booted
        const status = error.message.split(' - ')[1];
        if (status && /404/gi.test(status)) {
            throw new Error('KERIA agent not booted', { cause: error });
        }

        // Agent was booted but cannot connect (possibly wrong passcode or corrupted state)
        throw new Error('Agent booted but cannot connect', { cause: error });
    });
};

/** Default retry interval for connection attempts (ms) */
const DEFAULT_CONNECT_RETRY_INTERVAL = 1000;

/** Maximum number of connection retry attempts */
const MAX_CONNECT_RETRIES = 5;

/**
 * Connect to an existing SignifyClient (no boot)
 * @param agentUrl - KERIA agent URL
 * @param passcode - 21-character passcode
 * @param retryInterval - Interval between retry attempts in ms (default: 1000)
 * @param maxRetries - Maximum number of retry attempts (default: 5)
 * @returns JSON string representation of connection result
 */
export const connect = async (
    agentUrl: string,
    passcode: string,
    retryInterval: number = DEFAULT_CONNECT_RETRY_INTERVAL,
    maxRetries: number = MAX_CONNECT_RETRIES
): Promise<string> => {
    _client = null;
    await ready();

    // TODO P2: Consider raising Tier for production use
    _client = new SignifyClient(agentUrl, passcode, Tier.low, '');

    let lastError: Error | null = null;
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            await connectSignifyClient();
            const state = await getState();
            return objectToJson({ success: true, state: JSON.parse(state) });
        } catch (error) {
            lastError = error instanceof Error ? error : new Error(String(error));
            console.warn(`signifyClient: connect attempt ${attempt}/${maxRetries} failed`, error);

            if (attempt < maxRetries) {
                await sleep(retryInterval);
            }
        }
    }

    // All retries exhausted
    console.error('signifyClient: connect failed after all retries', lastError);
    _client = null;
    throw lastError ?? new Error('Connect failed after all retries');
};

/**
 * Get the current client state (controller and agent info)
 * @returns JSON string of ClientState
 */
export const getState = async (): Promise<string> => {
    try {
        const client = await validateClient();
        const state = await client.state();
        return objectToJson(state);
    } catch (error) {
        console.error('signifyClient: getState error:', error);
        throw error;
    }
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
        async (client) => {
            const res: EventResult = await client.identifiers().create(name);
            const op = await res.op();
            const prefix = op.response.i as string;
            return prefix;
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
        async (client) => {
            const managedIdentifiers = await client.identifiers().list();
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
        (client) => client.identifiers().get(name),
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
        async (client) => {
            const aid: HabState = await client.identifiers().get(prefix);
            return aid.name ?? '';
        },
        { Prefix: prefix }
    );
    // Return the raw string, not JSON-encoded
    return JSON.parse(result) as string;
};

/**
 * Get full identifier object by prefix
 * @param prefix - AID prefix
 * @returns JSON string of HabState object
 */
export const getIdentifierByPrefix = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'getIdentifierByPrefix',
        async (client) => {
            const aid: HabState = await client.identifiers().get(prefix);
            return aid;
        },
        { Prefix: prefix }
    );
};

/**
 * Rename an identifier (update its alias/name)
 * @param currentName - Current name/alias of the identifier
 * @param newName - New name/alias for the identifier
 * @returns JSON string of HabState after update
 */
export const renameAID = async (currentName: string, newName: string): Promise<string> => {
    return withClientOperation(
        'renameAID',
        async (client) => {
            const aid: HabState = await client.identifiers().update(currentName, { name: newName });
            return aid;
        },
        { 'Old name': currentName, 'New name': newName }
    );
};

// ===================== Credential (ACDC) Operations =====================

/**
 * List all credentials
 * @returns JSON array of CredentialResult objects
 */
export const getCredentialsList = async (): Promise<string> => {
    return withClientOperation(
        'getCredentialsList',
        async (client) => {
            const credentials: CredentialResult[] = await client.credentials().list();
            return credentials;
        },
        { Count: 'retrieved' }
    );
};

/**
 * Get a specific credential by SAID
 * @param id - Credential SAID
 * @param includeCESR - Include CESR encoding (returns string if true, CredentialResult if false)
 * @returns JSON string of CredentialResult or CESR string
 */
export const getCredential = async (
    id: string,
    includeCESR: boolean = false
): Promise<string> => {
    return withClientOperation(
        'getCredential',
        async (client) => {
            if (includeCESR) {
                const cesrResult: string = await client.credentials().get(id, true);
                return cesrResult;
            } else {
                const credResult: CredentialResult = await client.credentials().get(id, false);
                return credResult;
            }
        },
        { SAID: id, IncludeCESR: includeCESR }
    );
};

/**
 * Issue a new credential
 * @param name - Identifier name
 * @param argsJson - JSON string of CredentialData
 * @returns JSON string of IssueCredentialResult
 */
export const credentialsIssue = async (name: string, argsJson: string): Promise<string> => {
    return withClientOperation(
        'credentialsIssue',
        async (client) => {
            const args = JSON.parse(argsJson) as CredentialData;
            const result: IssueCredentialResult = await client.credentials().issue(name, args);
            return result;
        },
        { Name: name }
    );
};

/**
 * Revoke a credential
 * @param name - Identifier name
 * @param said - Credential SAID
 * @param datetime - Optional datetime for revocation
 * @returns JSON string of RevokeCredentialResult
 */
export const credentialsRevoke = async (name: string, said: string, datetime?: string): Promise<string> => {
    return withClientOperation(
        'credentialsRevoke',
        async (client) => {
            const result: RevokeCredentialResult = await client.credentials().revoke(name, said, datetime);
            return result;
        },
        { SAID: said }
    );
};

/**
 * Get credential state
 * @param ri - Registry identifier
 * @param said - Credential SAID
 * @returns JSON string of CredentialState
 */
export const credentialsState = async (ri: string, said: string): Promise<string> => {
    return withClientOperation(
        'credentialsState',
        async (client) => {
            const state: CredentialState = await client.credentials().state(ri, said);
            return state;
        },
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
        async (client) => {
            await client.credentials().delete(said);
            return { success: true, message: `Credential ${said} deleted successfully` };
        },
        { SAID: said }
    );
};

// ===================== Signed Headers =====================

/**
 * Get signed headers for HTTP request authentication
 * @param _origin - Origin URL (unused, kept for API compatibility)
 * @param url - Resource URL
 * @param method - HTTP method
 * @param headersDict - Initial headers object
 * @param aidName - Identifier name for signing
 * @returns JSON object of signed headers
 */
export const getSignedHeaders = async (
    _origin: string,
    url: string,
    method: string,
    headersDict: { [key: string]: string },
    aidName: string
): Promise<{ [key: string]: string }> => {
    try {
        const client = await validateClient();

        const requestInit: RequestInit = {
            method,
            headers: headersDict
        };
        const signedRequest: Request = await client.createSignedRequest(aidName, url, requestInit);

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
    (client) => (args) => client.ipex().apply(args)
);

export const ipexOffer = createIpexMethod<IpexOfferArgs>(
    'Offer',
    (client) => (args) => client.ipex().offer(args)
);

export const ipexAgree = createIpexMethod<IpexAgreeArgs>(
    'Agree',
    (client) => (args) => client.ipex().agree(args)
);

export const ipexGrant = createIpexMethod<IpexGrantArgs>(
    'Grant',
    (client) => (args) => client.ipex().grant(args)
);

export const ipexAdmit = createIpexMethod<IpexAdmitArgs>(
    'Admit',
    (client) => (args) => client.ipex().admit(args)
);

export const ipexSubmitApply = createIpexSubmitMethod(
    'SubmitApply',
    (client) => (name, exn, sigs, recipients) => client.ipex().submitApply(name, exn, sigs, recipients),
    false
);

export const ipexSubmitOffer = createIpexSubmitMethod(
    'SubmitOffer',
    (client) => (name, exn, sigs, atc, recipients) => client.ipex().submitOffer(name, exn, sigs, atc, recipients),
    true
);

export const ipexSubmitAgree = createIpexSubmitMethod(
    'SubmitAgree',
    (client) => (name, exn, sigs, recipients) => client.ipex().submitAgree(name, exn, sigs, recipients),
    false
);

export const ipexSubmitGrant = createIpexSubmitMethod(
    'SubmitGrant',
    (client) => (name, exn, sigs, atc, recipients) => client.ipex().submitGrant(name, exn, sigs, atc, recipients),
    true
);

export const ipexSubmitAdmit = createIpexSubmitMethod(
    'SubmitAdmit',
    (client) => (name, exn, sigs, atc, recipients) => client.ipex().submitAdmit(name, exn, sigs, atc, recipients),
    true
);

// ===================== OOBI Operations =====================

export const oobiGet = async (name: string, role?: string): Promise<string> => {
    return withClientOperation(
        'oobiGet',
        (client) => client.oobis().get(name, role),
        { Name: name, Role: role }
    );
};

export const oobiResolve = async (oobi: string, alias?: string): Promise<string> => {
    return withClientOperation(
        'oobiResolve',
        (client) => client.oobis().resolve(oobi, alias),
        { Alias: alias }
    );
};

// ===================== Operations Management =====================

export const operationsGet = async <T = unknown>(name: string): Promise<string> => {
    return withClientOperation(
        'operationsGet',
        (client) => client.operations().get<T>(name),
        { Name: name }
    );
};

export const operationsList = async (type?: string): Promise<string> => {
    return withClientOperation(
        'operationsList',
        (client) => client.operations().list(type),
        { Type: type }
    );
};

export const operationsDelete = async (name: string): Promise<string> => {
    return withClientOperation(
        'operationsDelete',
        async (client) => {
            await client.operations().delete(name);
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
        async (client) => {
            const operation = JSON.parse(operationJson) as Operation<T>;
            const options = optionsJson ? JSON.parse(optionsJson) : undefined;
            return await client.operations().wait<T>(operation, options);
        }
    );
};

// ===================== Registry Management =====================

export const registriesList = async (name: string): Promise<string> => {
    return withClientOperation(
        'registriesList',
        async (client) => {
            const registries: Registry[] = await client.registries().list(name);
            return registries;
        },
        { Name: name }
    );
};

export const registriesCreate = async (argsJson: string): Promise<string> => {
    return withClientOperation(
        'registriesCreate',
        async (client) => {
            const args = JSON.parse(argsJson) as CreateRegistryArgs;
            const result: RegistryResult = await client.registries().create(args);
            // Return the registry after waiting for operation
            const registry: Registry = await result.op();
            return registry;
        }
    );
};

export const registriesRename = async (name: string, registryName: string, newName: string): Promise<string> => {
    return withClientOperation(
        'registriesRename',
        async (client) => {
            const registry: Registry = await client.registries().rename(name, registryName, newName);
            return registry;
        },
        { 'New name': newName }
    );
};

// ===================== Contact Management =====================

export const contactsList = async (group?: string, filterField?: string, filterValue?: string): Promise<string> => {
    return withClientOperation(
        'contactsList',
        async (client) => {
            const contacts: Contact[] = await client.contacts().list(group, filterField, filterValue);
            return contacts;
        }
    );
};

export const contactsGet = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'contactsGet',
        async (client) => {
            const contact: Contact = await client.contacts().get(prefix);
            return contact;
        },
        { Prefix: prefix }
    );
};

export const contactsAdd = async (prefix: string, infoJson: string): Promise<string> => {
    return withClientOperation(
        'contactsAdd',
        async (client) => {
            const info = JSON.parse(infoJson) as ContactInfo;
            const contact: Contact = await client.contacts().add(prefix, info);
            return contact;
        },
        { Prefix: prefix }
    );
};

export const contactsUpdate = async (prefix: string, infoJson: string): Promise<string> => {
    return withClientOperation(
        'contactsUpdate',
        async (client) => {
            const info = JSON.parse(infoJson) as ContactInfo;
            const contact: Contact = await client.contacts().update(prefix, info);
            return contact;
        },
        { Prefix: prefix }
    );
};

export const contactsDelete = async (prefix: string): Promise<string> => {
    return withClientOperation(
        'contactsDelete',
        async (client) => {
            await client.contacts().delete(prefix);
            return { success: true, message: `Contact ${prefix} deleted successfully` };
        },
        { Prefix: prefix }
    );
};

// ===================== Schema Operations =====================

export const schemasGet = async (said: string): Promise<string> => {
    return withClientOperation(
        'schemasGet',
        async (client) => {
            const schema: Schema = await client.schemas().get(said);
            return schema;
        },
        { SAID: said }
    );
};

export const schemasList = async (): Promise<string> => {
    return withClientOperation(
        'schemasList',
        async (client) => {
            const schemas: Schema[] = await client.schemas().list();
            return schemas;
        }
    );
};

// ===================== Notifications Operations =====================

export const notificationsList = async (start?: number, end?: number): Promise<string> => {
    return withClientOperation(
        'notificationsList',
        (client) => client.notifications().list(start, end)
    );
};

export const notificationsMark = async (said: string): Promise<string> => {
    try {
        const client = await validateClient();
        const result = await client.notifications().mark(said);
        console.debug('signifyClient: notificationsMark - SAID:', said);
        return result; // Already a string
    } catch (error) {
        console.error('signifyClient: notificationsMark error:', error);
        throw error;
    }
};

export const notificationsDelete = async (said: string): Promise<string> => {
    return withClientOperation(
        'notificationsDelete',
        async (client) => {
            // Validation already done by withClientOperation wrapper
            await client.notifications().delete(said);
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
        (client) => client.escrows().listReply(route),
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
        (client) => client.groups().getRequest(said),
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
        async (client) => {
            const exn = JSON.parse(exnJson);
            const sigs = JSON.parse(sigsJson) as string[];
            return await client.groups().sendRequest(name, exn, sigs, atc);
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
        async (client) => {
            const rot = JSON.parse(rotJson);
            const sigs = JSON.parse(sigsJson);
            const smids = JSON.parse(smidsJson) as string[];
            const rmids = JSON.parse(rmidsJson) as string[];
            return await client.groups().join(name, rot, sigs, gid, smids, rmids);
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
        (client) => client.exchanges().get(said),
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
        async (client) => {
            const sender = JSON.parse(senderJson);
            const payload = JSON.parse(payloadJson);
            const embeds = JSON.parse(embedsJson);
            const recipients = JSON.parse(recipientsJson) as string[];
            return await client.exchanges().send(name, topic, sender, route, payload, embeds, recipients);
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
        async (client) => {
            const exn = JSON.parse(exnJson) as Serder;
            const sigs = JSON.parse(sigsJson) as string[];
            const recipients = JSON.parse(recipientsJson) as string[];
            return await client.exchanges().sendFromEvents(name, topic, exn, sigs, atc, recipients);
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
        async (client) => {
            const data = dataJson ? JSON.parse(dataJson) : undefined;
            return await client.delegations().approve(name, data);
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
        (client) => client.keyEvents().get(prefix),
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
        (client) => client.keyStates().get(prefix),
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
        async (client) => {
            const prefixes = JSON.parse(prefixesJson) as string[];
            return await client.keyStates().list(prefixes);
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
        async (client) => {
            const anchor = anchorJson ? JSON.parse(anchorJson) : undefined;
            return await client.keyStates().query(prefix, sn, anchor);
        },
        { Prefix: prefix, SN: sn }
    );
};

// ===================== Config Operations =====================

/**
 * Get agent configuration
 * @returns JSON string of AgentConfig
 */
export const configGet = async (): Promise<string> => {
    return withClientOperation(
        'configGet',
        async (client) => {
            const config: AgentConfig = await client.config().get();
            return config;
        }
    );
};

// ===================== Challenges Operations =====================

/**
 * Generate a random challenge word list based on BIP39
 * @param strength - Integer representing the strength of the challenge (128 or 256)
 * @returns JSON string of Challenge with words array
 */
export const challengesGenerate = async (strength: number = 128): Promise<string> => {
    return withClientOperation(
        'challengesGenerate',
        async (client) => {
            const challenge: Challenge = await client.challenges().generate(strength);
            return challenge;
        },
        { Strength: strength }
    );
};

/**
 * Respond to a challenge by signing a message with the list of words
 * @param name - Name or alias of the identifier
 * @param recipient - Prefix of the recipient of the response
 * @param wordsJson - JSON string array of words to embed in signed response
 * @returns JSON string of response result
 */
export const challengesRespond = async (name: string, recipient: string, wordsJson: string): Promise<string> => {
    return withClientOperation(
        'challengesRespond',
        async (client) => {
            const words = JSON.parse(wordsJson) as string[];
            const result = await client.challenges().respond(name, recipient, words);
            return result;
        },
        { Name: name, Recipient: recipient }
    );
};

/**
 * Ask Agent to verify a given sender signed the provided words
 * @param source - Prefix of the identifier that was challenged
 * @param wordsJson - JSON string array of challenge words to check
 * @returns JSON string of Operation
 */
export const challengesVerify = async (source: string, wordsJson: string): Promise<string> => {
    return withClientOperation(
        'challengesVerify',
        async (client) => {
            const words = JSON.parse(wordsJson) as string[];
            const op: Operation<unknown> = await client.challenges().verify(source, words);
            return op;
        },
        { Source: source }
    );
};

/**
 * Mark challenge response as signed and accepted
 * @param source - Prefix of the identifier that was challenged
 * @param said - qb64 AID of exn message representing the signed response
 * @returns JSON string of response result
 */
export const challengesResponded = async (source: string, said: string): Promise<string> => {
    return withClientOperation(
        'challengesResponded',
        async (client) => {
            const response = await client.challenges().responded(source, said);
            return { ok: response.ok, status: response.status };
        },
        { Source: source, SAID: said }
    );
};

// ===================== Arbitrary Data Signing =====================

/**
 * Result item for signed data
 */
interface SignDataResultItem {
    data: string;
    signature: string;
}

/**
 * Result of signing data items
 */
interface SignDataResult {
    aid: string;
    items: SignDataResultItem[];
}

/**
 * Sign arbitrary data strings with an identifier
 * @param aidName - Name or prefix of the identifier to sign with
 * @param dataItems - Array of UTF-8 strings to sign
 * @returns JSON string of SignDataResult with aid prefix and signed items
 */
export const signData = async (aidName: string, dataItems: string[]): Promise<string> => {
    return withClientOperation(
        'signData',
        async (client) => {
            // Get the identifier
            const hab = await client.identifiers().get(aidName);
            if (!hab) {
                throw new Error(`Identifier '${aidName}' not found`);
            }

            // Get the keeper (signer) for this identifier
            const keeper = client.manager!.get(hab);
            if (!keeper) {
                throw new Error(`No keeper available for identifier '${aidName}'`);
            }

            // Sign each data item
            const signedItems: SignDataResultItem[] = [];
            for (const dataItem of dataItems) {
                // Convert string to Uint8Array
                const encoder = new TextEncoder();
                const dataBytes = encoder.encode(dataItem);

                // Sign the data - keeper.sign returns an array of signature strings
                const sigs = await keeper.sign(dataBytes);

                // Use the first signature (for non-multisig identifiers)
                const signature = sigs.length > 0 ? sigs[0] : '';

                signedItems.push({
                    data: dataItem,
                    signature: signature
                });
            }

            const result: SignDataResult = {
                aid: hab.prefix,
                items: signedItems
            };

            return result;
        },
        { AIDName: aidName, ItemCount: dataItems.length }
    );
};
