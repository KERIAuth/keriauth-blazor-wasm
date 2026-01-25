using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Models.Messages.CsBw;
using Extension.Models.Messages.ExCs;
using PortMessages = Extension.Models.Messages.Port;

namespace Extension.Tests.Contracts;

/// <summary>
/// Generates JSON fixtures for TypeScript contract tests.
/// These fixtures document the expected JSON structure of C# message types
/// and allow TypeScript tests to validate type compatibility.
///
/// Usage:
/// 1. Run the GenerateFixtures test to create/update fixture files
/// 2. TypeScript tests in scripts/types/src/__tests__/contracts/ validate against these fixtures
/// 3. If C# types change, regenerate fixtures and update TypeScript tests accordingly
/// </summary>
public class MessageContractFixtureGenerator {
    private static readonly string FixturesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Contracts", "Fixtures");

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>
    /// Generates all fixtures. Run this test to update fixtures after C# type changes.
    /// </summary>
    [Fact]
    public void GenerateAllFixtures() {
        Directory.CreateDirectory(FixturesDir);

        GenerateCsBwMethodTypesFixture();
        GenerateBwCsMessageTypesFixture();
        GeneratePortMessageTypesFixture();
        GenerateCsBwPortMessageTypesFixture();
        GeneratePortMessageFixtures();
        GenerateRpcRequestFixtures();
        GenerateRpcResponseFixtures();
    }

    /// <summary>
    /// Generates fixture documenting CsBwMessageTypes constants.
    /// TypeScript tests validate CsBwRpcMethods matches these values.
    /// </summary>
    private static void GenerateCsBwMethodTypesFixture() {
        var fixture = new Dictionary<string, string> {
            ["Authorize"] = CsBwMessageTypes.AUTHORIZE,
            ["SelectAuthorizeAid"] = CsBwMessageTypes.SELECT_AUTHORIZE_AID,
            ["SelectAuthorizeCredential"] = CsBwMessageTypes.SELECT_AUTHORIZE_CREDENTIAL,
            ["SignData"] = CsBwMessageTypes.SIGN_DATA,
            ["SignRequest"] = CsBwMessageTypes.SIGN_REQUEST,
            ["CreateDataAttestation"] = CsBwMessageTypes.CREATE_DATA_ATTESTATION,
            ["GetCredential"] = CsBwMessageTypes.GET_CREDENTIAL,
            ["ConfigureVendor"] = CsBwMessageTypes.CONFIGURE_VENDOR,
            ["SignifyExtension"] = CsBwMessageTypes.SIGNIFY_EXTENSION,
            ["SignifyExtensionClient"] = CsBwMessageTypes.SIGNIFY_EXTENSION_CLIENT,
            ["GetSessionInfo"] = CsBwMessageTypes.GET_SESSION_INFO,
            ["ClearSession"] = CsBwMessageTypes.CLEAR_SESSION,
            ["Init"] = CsBwMessageTypes.INIT
        };

        WriteFixture("CsBwMethodTypes.json", fixture);
    }

    /// <summary>
    /// Generates fixture documenting BwCsMessageTypes constants.
    /// TypeScript tests validate BwCsMessageTypes matches these values.
    /// </summary>
    private static void GenerateBwCsMessageTypesFixture() {
        var fixture = new Dictionary<string, string> {
            ["Ready"] = BwCsMessageTypes.READY,
            ["Reply"] = BwCsMessageTypes.REPLY,
            ["ReplyCanceled"] = BwCsMessageTypes.REPLY_CANCELED,
            ["ReplyCredential"] = BwCsMessageTypes.REPLY_CREDENTIAL,
            ["FromBackgroundWorker"] = BwCsMessageTypes.FROM_BACKGROUND_WORKER,
            ["AppClosed"] = BwCsMessageTypes.APP_CLOSED
        };

        WriteFixture("BwCsMessageTypes.json", fixture);
    }

    /// <summary>
    /// Generates fixture for generic port message type discriminators.
    /// TypeScript tests validate PortMessageTypes matches these values.
    /// </summary>
    private static void GeneratePortMessageTypesFixture() {
        var fixture = new Dictionary<string, string> {
            ["Hello"] = PortMessages.PortMessageTypes.Hello,
            ["Ready"] = PortMessages.PortMessageTypes.Ready,
            ["AttachTab"] = PortMessages.PortMessageTypes.AttachTab,
            ["DetachTab"] = PortMessages.PortMessageTypes.DetachTab,
            ["Event"] = PortMessages.PortMessageTypes.Event,
            ["RpcRequest"] = PortMessages.PortMessageTypes.RpcRequest,
            ["RpcResponse"] = PortMessages.PortMessageTypes.RpcResponse,
            ["Error"] = PortMessages.PortMessageTypes.Error
        };

        WriteFixture("PortMessageTypes.json", fixture);
    }

    /// <summary>
    /// Generates fixture for directional CS→BW port message type discriminators.
    /// TypeScript tests validate CsBwPortMessageTypes matches these values.
    /// </summary>
    private static void GenerateCsBwPortMessageTypesFixture() {
        var fixture = new Dictionary<string, string> {
            ["RpcRequest"] = PortMessages.CsBwPortMessageTypes.RpcRequest,
            ["RpcResponse"] = PortMessages.CsBwPortMessageTypes.RpcResponse
        };

        WriteFixture("CsBwPortMessageTypes.json", fixture);
    }

    /// <summary>
    /// Generates fixture for port message types (HELLO, READY, RPC_REQ, etc.)
    /// TypeScript tests validate PortMessageTypes matches these structures.
    /// </summary>
    private static void GeneratePortMessageFixtures() {
        // HelloMessage
        var helloMessage = new PortMessages.HelloMessage {
            Context = PortMessages.ContextKind.ContentScript,
            InstanceId = "test-instance-id-12345",
            TabId = 42,
            FrameId = 0
        };
        WriteFixture("PortMessage_Hello.json", helloMessage);

        // ReadyMessage
        var readyMessage = new PortMessages.ReadyMessage {
            PortSessionId = "port-session-abc123",
            TabId = 42,
            FrameId = 0
        };
        WriteFixture("PortMessage_Ready.json", readyMessage);

        // AttachTabMessage
        var attachTabMessage = new PortMessages.AttachTabMessage {
            TabId = 42,
            FrameId = 0
        };
        WriteFixture("PortMessage_AttachTab.json", attachTabMessage);

        // DetachTabMessage
        var detachTabMessage = new PortMessages.DetachTabMessage();
        WriteFixture("PortMessage_DetachTab.json", detachTabMessage);

        // ErrorMessage
        var errorMessage = new PortMessages.ErrorMessage {
            Code = PortMessages.PortErrorCodes.AttachFailed,
            Message = "Failed to attach to tab: tab not found"
        };
        WriteFixture("PortMessage_Error.json", errorMessage);
    }

    /// <summary>
    /// Generates fixtures for RpcRequest messages.
    /// TypeScript tests validate RpcRequest structure and method-specific params.
    /// </summary>
    private static void GenerateRpcRequestFixtures() {
        // Authorize request
        var authorizeRequest = new PortMessages.RpcRequest {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-001",
            Method = CsBwMessageTypes.AUTHORIZE,
            Params = new {
                message = "Please authorize this action"
            }
        };
        WriteFixture("RpcRequest_Authorize.json", authorizeRequest);

        // SignData request
        var signDataRequest = new PortMessages.RpcRequest {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-002",
            Method = CsBwMessageTypes.SIGN_DATA,
            Params = new {
                message = "Sign the following data",
                items = new[] { "data item 1", "data item 2" }
            }
        };
        WriteFixture("RpcRequest_SignData.json", signDataRequest);

        // SignRequest request
        var signRequestRequest = new PortMessages.RpcRequest {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-003",
            Method = CsBwMessageTypes.SIGN_REQUEST,
            Params = new {
                url = "https://api.example.com/resource",
                method = "POST",
                headers = new Dictionary<string, string> {
                    ["content-type"] = "application/json"
                }
            }
        };
        WriteFixture("RpcRequest_SignRequest.json", signRequestRequest);

        // CreateDataAttestation request
        var createCredRequest = new PortMessages.RpcRequest {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-004",
            Method = CsBwMessageTypes.CREATE_DATA_ATTESTATION,
            Params = new {
                credData = new Dictionary<string, object> {
                    ["field1"] = "value1",
                    ["field2"] = 123
                },
                schemaSaid = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao"
            }
        };
        WriteFixture("RpcRequest_CreateDataAttestation.json", createCredRequest);

        // SignifyExtension query
        var extensionQuery = new PortMessages.RpcRequest {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-005",
            Method = CsBwMessageTypes.SIGNIFY_EXTENSION
        };
        WriteFixture("RpcRequest_SignifyExtension.json", extensionQuery);
    }

    /// <summary>
    /// Generates fixtures for RpcResponse messages.
    /// TypeScript tests validate RpcResponse structure and method-specific results.
    /// </summary>
    private static void GenerateRpcResponseFixtures() {
        // Success response with authorize result
        var authorizeSuccessResponse = new PortMessages.RpcResponse {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-001",
            Ok = true,
            Result = new {
                identifier = new {
                    prefix = "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c"
                }
            }
        };
        WriteFixture("RpcResponse_AuthorizeSuccess.json", authorizeSuccessResponse);

        // Success response with credential
        var credentialSuccessResponse = new PortMessages.RpcResponse {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-002",
            Ok = true,
            Result = new {
                credential = new {
                    raw = new Dictionary<string, object> {
                        ["v"] = "ACDC10JSON000197_",
                        ["d"] = "EHMnCf8_nIemuPx-cUHb1k5DsT8K09vqx0bSwNRr9S4c",
                        ["i"] = "EEXekkGu9IAzav6pZVJhkLnjtjM5v3AcyA-pdKUcaGei"
                    },
                    cesr = "-FABEHM..."
                }
            }
        };
        WriteFixture("RpcResponse_CredentialSuccess.json", credentialSuccessResponse);

        // Success response for sign-data
        var signDataSuccessResponse = new PortMessages.RpcResponse {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-003",
            Ok = true,
            Result = new {
                aid = "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c",
                items = new[] {
                    new { data = "data item 1", signature = "0BAg..." },
                    new { data = "data item 2", signature = "0BCh..." }
                }
            }
        };
        WriteFixture("RpcResponse_SignDataSuccess.json", signDataSuccessResponse);

        // Error response
        var errorResponse = new PortMessages.RpcResponse {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-004",
            Ok = false,
            Error = "User canceled the operation"
        };
        WriteFixture("RpcResponse_Error.json", errorResponse);

        // Extension ID response
        var extensionIdResponse = new PortMessages.RpcResponse {
            PortSessionId = "port-session-abc123",
            Id = "rpc-request-id-005",
            Ok = true,
            Result = new {
                extensionId = "abcdefghijklmnopqrstuvwxyz123456"
            }
        };
        WriteFixture("RpcResponse_ExtensionId.json", extensionIdResponse);
    }

    private static void WriteFixture(string filename, object data) {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var path = Path.Combine(FixturesDir, filename);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Validates that all expected fixtures exist.
    /// Run this test to verify fixtures are in sync.
    /// </summary>
    [Fact]
    public void ValidateFixturesExist() {
        var expectedFixtures = new[] {
            "CsBwMethodTypes.json",
            "BwCsMessageTypes.json",
            "PortMessageTypes.json",
            "CsBwPortMessageTypes.json",
            "PortMessage_Hello.json",
            "PortMessage_Ready.json",
            "PortMessage_AttachTab.json",
            "PortMessage_DetachTab.json",
            "PortMessage_Error.json",
            "RpcRequest_Authorize.json",
            "RpcRequest_SignData.json",
            "RpcRequest_SignRequest.json",
            "RpcRequest_CreateDataAttestation.json",
            "RpcRequest_SignifyExtension.json",
            "RpcResponse_AuthorizeSuccess.json",
            "RpcResponse_CredentialSuccess.json",
            "RpcResponse_SignDataSuccess.json",
            "RpcResponse_Error.json",
            "RpcResponse_ExtensionId.json"
        };

        foreach (var fixture in expectedFixtures) {
            var path = Path.Combine(FixturesDir, fixture);
            Assert.True(File.Exists(path), $"Fixture missing: {fixture}. Run GenerateAllFixtures to create.");
        }
    }
}
