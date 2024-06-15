export declare const PASSCODE_TIMEOUT = 5;
/**
 * Connect or boot a SignifyClient instance
 * @param agentUrl
 * @param bootUrl
 * @param passcode
 * @returns
 */
export declare const bootAndConnect: (agentUrl: string, bootUrl: string, passcode: string) => Promise<string>;
export declare const connect: (agentUrl: string, passcode: string) => Promise<{
    error: unknown;
} | undefined>;
export declare function createAID(name: string): Promise<string>;
export declare const getAIDs: () => Promise<string>;
