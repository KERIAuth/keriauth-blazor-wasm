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
    Identifier
} from "@signify-ts";

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
    _client = new SignifyClient(agentUrl, passcode, Tier.low, bootUrl);

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

    // TODO EE! remove temporary test:
    console.warn(new Date().toISOString())

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
        const id: string = op2.response.i;
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
    const identifierJson: string = JSON.stringify(managedIdentifiers);
    console.debug("signify_ts_shim: getAIDs: ", managedIdentifiers);
    return identifierJson;
}

// get AID by name, i.e., alias not prefix
export const getAID = async (name: string): Promise<string> => {
    try {
        validateClient();
        const client: SignifyClient = _client!;
        const managedIdentifier = await client.identifiers().get(name);
        const identifierJson: string = JSON.stringify(managedIdentifier);
        console.debug("signify_ts_shim: getAID: name, identifier:", name, managedIdentifier);
        return identifierJson;
    } catch (error) {
        console.error("signify_ts_shim: getAID: name, error:", name, error);
        throw error;
    }
}

export async function getCredentialsList(
    // filter: object
): Promise<string> {  // TODO : define the return type
    try {
        validateClient();
        const client: SignifyClient = _client!;
        const credentials: EventResult = await client.credentials().list();
        console.log("signify_ts_shim: getCredentialList credentials: ", credentials);
        const credentialsJson: string = JSON.stringify(credentials);
        return credentialsJson;
    }
    catch (error) {
        console.error(error);
        throw error;
    }
}

export async function getCredential(
    id: string,
    includeCESR: boolean = false
): Promise<any> {
    try {
        validateClient();
        const client: SignifyClient = _client!;
        const credential = await client.credentials().get(id, includeCESR);
        console.log("signify_ts_shim: getCredential: ", credential);
        return credential as any;
    }
    catch (error) {
        console.error(error);
        throw error;
    }
}

// inspired by https://github.com/WebOfTrust/signify-browser-extension/blob/d51ba75a3258a7a29267044235b915e1d0444075/src/pages/background/services/signify.ts#L307
/**
   * @param origin - origin url from where request is being made -- required
   * @param rurl - resource url that the request is being made to -- required
   * @param method - http method of the request -- default GET
   * @param headers - initialHeaders object of the request -- default empty
   * @param signin - signin object containing identifier or credential -- required
   * @returns Promise<Request> - returns a signed initialHeaders request object
   */
const getSignedHeaders = async (
    origin: string,
    rurl: string,
    method: string,
    headers: Headers,
    aidName: string,
): Promise<Request> => {
    // TODO not that headers won't be printable this way:
    console.log("getSignedHeaders: params: ", origin, " ", rurl, " ", method, " ", headers, " ", aidName);

    // in case the client is not connected, try to connect
    //const connected = await isConnected();
    // connected is false, it means the client session timed out or disconnected by user
    //if (!connected) {
    validateClient();
    //}

    const client: SignifyClient = _client!;

    //const session = await sessionService.get({ tabId, origin });
    //await sessionService.incrementRequestCount(tabId);
    //if (!session) {
    //    throw new Error("Session not found");
    //}
    try {
        // TODO not that headers won't be printable this way:
        console.log("getSignedHeaders: createSignedRequest args:", aidName, rurl, method, headers);
        const signedRequest: Request = await client.createSignedRequest(aidName, rurl, {
            method,
            headers,
        });
        //resetTimeoutAlarm();
        console.log("getSignedHeaders: signedRequest:", signedRequest);

        // Log each header for better visibility
        if (signedRequest.headers) {
            console.log("getSignedHeaders: signedRequest.headers details:");
            signedRequest.headers.forEach((value, key) => {
                console.log(`    ${key}: ${value}`);
            });
        }
        
        //let jsonHeaders: { [key: string]: string } = {};
        //if (signedRequest?.headers) {
        //    for (const pair of signedRequest.headers.entries()) {
        //        jsonHeaders[pair[0]] = String(pair[1]);
        //    }
        //}
        //console.log("getSignedHeaders: jsonHeaders:", jsonHeaders);

        //const nh = new Headers(jsonHeaders);
        // console.log("getSignedHeaders: newHeaders:", nh);
        return signedRequest;
        
        
    } catch (error) {
        console.error("getSignedHeaders: Error occurred:", error);
        throw error;
    }
};

export function parseHeaders(headersJson: string | null): Headers {
    try {
        // If headersJson is null, return an empty Headers object
        if (!headersJson) {
            console.log("parseHeaders: null new Headers: ", new Headers());
            return new Headers();
        }

        // Try to parse the JSON string
        const headersObj: Record<string, string> = JSON.parse(headersJson);

        // Check if the parsed result is a plain object
        if (typeof headersObj !== 'object' || headersObj === null) {
            throw new Error("Invalid headers format");
        }

        // Convert the plain object to a Headers object
        console.log("parseHeaders: headersObj: ", headersObj, " newHeaders: ", new Headers(headersObj));
        return new Headers(headersObj);
    } catch (error) {
        console.error("Failed to parse headersJson:", error);
        // Return an empty Headers object in case of failure
        return new Headers();
    }
}


//[JSImport("getSignedHeadersWithJsonHeaders", "signify_ts_shim")]
//        internal static partial Task < string > GetSignedHeadersWithJsonHeaders(string origin, string rurl, string method, string jsonHeaders, string aidName);
//    }
// Returns a json string of signed Headers
export const getSignedHeadersWithJsonHeaders = async (
    origin: string,
    rurl: string,
    method: string,
    headersJson: string,
    aidName: string,
): Promise<string> => {
    try {
        console.log("getSignedHeadersWithJsonHeaders: ", origin, " ", rurl, " ", method, " ", headersJson, " ", aidName);
        const initialHeaders: Headers = parseHeaders(headersJson);
        // TODO to confirm headers parsing, iterate and print these, but expect these to be {} at current stage of development.
        console.log("getSignedHeadersWithJsonHeaders initialHeaders: ", initialHeaders);

        // Call the original getSignedHeaders function with the parsed initialHeaders
        const signedRequest: Request = await getSignedHeaders(
            origin,
            rurl,
            method,
            initialHeaders,
            aidName,
        );
        console.log("getSignedHeadersWithJsonHeaders signedHeaders: ", signedRequest);

        // Convert the returned Headers object back into a plain object for JSON serialization
        const signedHeadersJson = headersToJsonBase64(signedRequest?.headers);
        
        
        //const headersPlainObject: { [key: string]: string } = { };
        //signedRequest.forEach((value: string, key: string) => {
        //    headersPlainObject[key] = value;
        //});

        // Return the plain object as a JSON string
        // return signedHeadersJson; // JSON.stringify(headersPlainObject);
    return signedHeadersJson;
    } catch (error) {
        // Handle errors (e.g., invalid JSON, issues with the request)
        console.error("Error occurred:", error);
        return JSON.stringify({ error: error.message });
    }
}

function headersToJsonBase64(headers: Headers): string {
    // Step 1: Convert Headers to a key-value object
    const headersObject: { [key: string]: string } = {};
    headers.forEach((value, key) => {
        headersObject[key] = value;
    });

    // Step 2: Convert the object to a JSON string
    const jsonString = JSON.stringify(headersObject);

    // Step 3: Convert JSON string to Base64 using btoa()
    const base64String = btoa(jsonString);

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