// Contract validation file to ensure C# models match TypeScript interfaces
// This file should compile without errors if the types are compatible

import type {
    ICsBwMsg,
    IBwCsMsg,
    IBwCsMsgPong,
    IIdentifier,
    ICredential,
    ISignature,
    IReplyMessageData,
    ISignin,
    IApprovedSignRequest
} from '../../Extension/wwwroot/scripts/es6/ExCsInterfaces';

// Import enums as values (not types)
import { CsBwMsgEnum, BwCsMsgEnum } from '../../Extension/wwwroot/scripts/es6/ExCsInterfaces';

// Test data matching C# test cases to ensure compatibility

// CsBwMsg Tests
const csBwMsg1: ICsBwMsg = {
    type: CsBwMsgEnum.POLARIS_SIGNIFY_AUTHORIZE,
    requestId: "req-123",
    payload: { test: "data" }
};

const csBwMsg2: ICsBwMsg = {
    type: CsBwMsgEnum.INIT
    // requestId and payload are optional
};

// BwCsMsg Tests
const bwCsMsg1: IBwCsMsg = {
    type: BwCsMsgEnum.REPLY,
    requestId: "req-456",
    payload: { result: "success" }
};

const bwCsMsg2: IBwCsMsg = {
    type: BwCsMsgEnum.REPLY_CANCELED,
    requestId: "req-789",
    error: "User canceled the operation"
};

const bwCsMsgPong: IBwCsMsgPong = {
    type: BwCsMsgEnum.READY
};

// Identifier Tests
const identifier1: IIdentifier = {
    prefix: "EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A",
    name: "My Identifier"
};

const identifier2: IIdentifier = {
    prefix: "EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A"
    // name is optional
};

// Complex Credential Test
const credential: ICredential = {
    issueeName: "Test Issuer",
    ancatc: ["anchor1", "anchor2"],
    sad: {
        a: { i: "issuer-id" },
        d: "digest-value"
    },
    schema: {
        title: "Legal Entity vLEI",
        credentialType: "LegalEntityvLEICredential",
        description: "A vLEI credential for legal entities"
    },
    status: {
        et: "iss"
    },
    cesr: "cesr-data"
};

// Signature Test
const signature: ISignature = {
    headers: {
        "Signify-Resource": "EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A",
        "Signify-Timestamp": "2024-01-15T10:30:00.000000+00:00"
    },
    identifier: {
        prefix: "EIDPKZjEBB2yh",
        name: "Test ID"
    },
    autoSignin: true
};

// Signin Test
const signin: ISignin = {
    id: "signin-123",
    domain: "example.com",
    createdAt: 1705318200000,
    updatedAt: 1705318300000,
    identifier: {
        prefix: "EIDPKZjEBB2yh",
        name: "User ID"
    },
    autoSignin: false
};

// ApprovedSignRequest Test
const approvedSignRequest: IApprovedSignRequest = {
    originStr: "https://example.com",
    url: "https://api.example.com/data",
    method: "POST",
    selectedName: "My Identity",
    initHeadersDict: {
        "Content-Type": "application/json",
        "Accept": "application/json"
    }
};

// Complex nested message test
const complexMessage: IBwCsMsg = {
    type: BwCsMsgEnum.REPLY,
    requestId: "complex-123",
    payload: {
        identifier: {
            prefix: "EIDPKZjEBB2yh",
            name: "Test ID"
        },
        credentials: [
            {
                id: "cred1",
                type: "vLEI"
            }
        ],
        nested: {
            deep: {
                value: 42
            }
        }
    }
};

// ReplyMessageData Test
const replyMessage: IReplyMessageData<IIdentifier> = {
    type: "/signify/reply",
    requestId: "reply-123",
    payload: {
        prefix: "EIDPKZjEBB2yh",
        name: "Reply ID"
    },
    payloadTypeName: "IIdentifier",
    source: "extension"
};

// Type assertion functions to validate contract at compile time
function assertCsBwMsgType(msg: ICsBwMsg): void {
    const { type, requestId, payload } = msg;
    console.assert(typeof type === 'string');
    if (requestId !== undefined) console.assert(typeof requestId === 'string');
    if (payload !== undefined) console.assert(typeof payload === 'object');
}

function assertBwCsMsgType(msg: IBwCsMsg): void {
    const { type, requestId, payload, error } = msg;
    console.assert(typeof type === 'string');
    if (requestId !== undefined) console.assert(typeof requestId === 'string');
    if (payload !== undefined) console.assert(typeof payload === 'object');
    if (error !== undefined) console.assert(typeof error === 'string');
}

function assertCredentialType(cred: ICredential): void {
    console.assert(typeof cred.issueeName === 'string');
    console.assert(Array.isArray(cred.ancatc));
    console.assert(typeof cred.sad === 'object');
    console.assert(typeof cred.sad.a === 'object');
    console.assert(typeof cred.sad.a.i === 'string');
    console.assert(typeof cred.sad.d === 'string');
    console.assert(typeof cred.schema === 'object');
    console.assert(typeof cred.status === 'object');
}

// JSON serialization contract tests
function testJsonContract(): void {
    // These should match the C# serialization format
    const jsonTests = [
        {
            name: "CsBwMsg minimal",
            obj: { type: "init" },
            expectedKeys: ["type"]
        },
        {
            name: "BwCsMsg with error",
            obj: { type: "reply_canceled", requestId: "req-789", error: "User canceled" },
            expectedKeys: ["type", "requestId", "error"]
        },
        {
            name: "Credential nested structure",
            obj: credential,
            expectedKeys: ["issueeName", "ancatc", "sad", "schema", "status", "cesr"]
        }
    ];

    jsonTests.forEach(test => {
        const json = JSON.stringify(test.obj);
        const parsed = JSON.parse(json);
        test.expectedKeys.forEach(key => {
            if (!(key in parsed)) {
                throw new Error(`Missing key '${key}' in ${test.name}`);
            }
        });
    });
}

// Export for testing
export {
    csBwMsg1,
    bwCsMsg1,
    credential,
    signature,
    signin,
    approvedSignRequest,
    complexMessage,
    assertCsBwMsgType,
    assertBwCsMsgType,
    assertCredentialType,
    testJsonContract
};