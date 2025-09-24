using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Models.ExCsMessages;
using Xunit;

namespace Extension.Tests.Models {
    public class ExCsMessagesTests {
        private readonly JsonSerializerOptions _jsonOptions;

        public ExCsMessagesTests() {
            _jsonOptions = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        #region CsBwMsg Tests

        [Fact]
        public void CsBwMsg_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new CsBwMsg(
                type: CsBwMsgTypes.POLARIS_SIGNIFY_AUTHORIZE,
                requestId: "req-123",
                payload: new { test = "data" }
            );

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);
            var expected = "{\"type\":\"/signify/authorize\",\"requestId\":\"req-123\",\"payload\":{\"test\":\"data\"}}";

            // Assert
            Assert.Equal(expected, json);
        }

        [Fact]
        public void CsBwMsg_ShouldDeserializeCorrectly() {
            // Arrange
            var json = "{\"type\":\"/signify/authorize\",\"requestId\":\"req-123\",\"payload\":{\"test\":\"data\"}}";

            // Act
            var msg = JsonSerializer.Deserialize<CsBwMsg>(json, _jsonOptions);

            // Assert
            Assert.NotNull(msg);
            Assert.Equal(CsBwMsgTypes.POLARIS_SIGNIFY_AUTHORIZE, msg.Type);
            Assert.Equal("req-123", msg.RequestId);
            Assert.NotNull(msg.Payload);
        }

        [Fact]
        public void CsBwMsg_WithNullOptionalFields_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new CsBwMsg(type: CsBwMsgTypes.INIT);

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Equal("{\"type\":\"init\"}", json);
        }

        #endregion

        #region BwCsMsg Tests

        [Fact]
        public void BwCsMsg_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new BwCsMsg(
                type: BwCsMsgTypes.REPLY,
                requestId: "req-456",
                payload: new { result = "success" },
                error: null
            );

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);
            var expected = "{\"type\":\"/signify/reply\",\"requestId\":\"req-456\",\"payload\":{\"result\":\"success\"}}";

            // Assert
            Assert.Equal(expected, json);
        }

        [Fact]
        public void BwCsMsg_WithError_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new BwCsMsg(
                type: BwCsMsgTypes.REPLY_CANCELED,
                requestId: "req-789",
                error: "User canceled the operation"
            );

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Contains("\"error\":\"User canceled the operation\"", json);
            Assert.Contains("\"type\":\"reply_canceled\"", json);
        }

        [Fact]
        public void BwCsMsgPong_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new BwCsMsgPong();

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Equal("{\"type\":\"ready\"}", json);
        }

        #endregion

        #region PortIdentifier Tests

        [Fact]
        public void PortIdentifier_ShouldSerializeCorrectly() {
            // Arrange
            var identifier = new PortIdentifier(
                prefix: "EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A",
                name: "My Identifier"
            );

            // Act
            var json = JsonSerializer.Serialize(identifier, _jsonOptions);

            // Assert
            Assert.Contains("\"prefix\":\"EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A\"", json);
            Assert.Contains("\"name\":\"My Identifier\"", json);
        }

        [Fact]
        public void PortIdentifier_WithoutName_ShouldSerializeCorrectly() {
            // Arrange
            var identifier = new PortIdentifier(prefix: "EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A");

            // Act
            var json = JsonSerializer.Serialize(identifier, _jsonOptions);

            // Assert
            Assert.Contains("\"prefix\":\"EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A\"", json);
            Assert.DoesNotContain("\"name\"", json);
        }

        #endregion

        #region PortCredential Tests

        private static readonly string[] TestAnchors = { "anchor1", "anchor2" };

        [Fact]
        public void PortCredential_ComplexNested_ShouldSerializeCorrectly() {
            // Arrange
            var credential = new PortCredential(
                issueeName: "Test Issuer",
                ancatc: TestAnchors,
                sad: new CredentialSad(
                    a: new CredentialSadA(i: "issuer-id"),
                    d: "digest-value"
                ),
                schema: new CredentialSchema(
                    title: "Legal Entity vLEI",
                    credentialType: "LegalEntityvLEICredential",
                    description: "A vLEI credential for legal entities"
                ),
                status: new CredentialStatus(et: "iss"),
                cesr: "cesr-data"
            );

            // Act
            var json = JsonSerializer.Serialize(credential, _jsonOptions);

            // Assert
            Assert.Contains("\"issueeName\":\"Test Issuer\"", json);
            Assert.Contains("\"ancatc\":[\"anchor1\",\"anchor2\"]", json);
            Assert.Contains("\"sad\":{\"a\":{\"i\":\"issuer-id\"},\"d\":\"digest-value\"}", json);
            Assert.Contains("\"credentialType\":\"LegalEntityvLEICredential\"", json);
            Assert.Contains("\"status\":{\"et\":\"iss\"}", json);
            Assert.Contains("\"cesr\":\"cesr-data\"", json);
        }

        [Fact]
        public void PortCredential_ShouldDeserializeCorrectly() {
            // Arrange
            var json = @"{
                ""issueeName"": ""Test Issuer"",
                ""ancatc"": [""anchor1"", ""anchor2""],
                ""sad"": {
                    ""a"": {""i"": ""issuer-id""},
                    ""d"": ""digest-value""
                },
                ""schema"": {
                    ""title"": ""Legal Entity vLEI"",
                    ""credentialType"": ""LegalEntityvLEICredential"",
                    ""description"": ""A vLEI credential""
                },
                ""status"": {
                    ""et"": ""iss""
                },
                ""cesr"": ""cesr-data""
            }";

            // Act
            var credential = JsonSerializer.Deserialize<PortCredential>(json, _jsonOptions);

            // Assert
            Assert.NotNull(credential);
            Assert.Equal("Test Issuer", credential.IssueeName);
            Assert.Equal(2, credential.Ancatc.Length);
            Assert.Equal("issuer-id", credential.Sad.A.I);
            Assert.Equal("digest-value", credential.Sad.D);
            Assert.Equal("LegalEntityvLEICredential", credential.Schema.CredentialType);
            Assert.Equal("iss", credential.Status.Et);
            Assert.Equal("cesr-data", credential.Cesr);
        }

        #endregion

        #region PortSignature Tests

        [Fact]
        public void PortSignature_ShouldSerializeCorrectly() {
            // Arrange
            var signature = new PortSignature(
                headers: new Dictionary<string, string> {
                    ["Signify-Resource"] = "EIDPKZjEBB2yh-XGIhYtx3D2c9pLWcJ2R4yyPVm9e7_A",
                    ["Signify-Timestamp"] = "2024-01-15T10:30:00.000000+00:00"
                },
                identifier: new PortIdentifierSimple(prefix: "EIDPKZjEBB2yh", name: "Test ID"),
                autoSignin: true
            );

            // Act
            var json = JsonSerializer.Serialize(signature, _jsonOptions);

            // Assert
            Assert.Contains("\"headers\":{", json);
            Assert.Contains("\"Signify-Resource\":\"EIDPKZjEBB2yh", json);
            Assert.Contains("\"autoSignin\":true", json);
            Assert.Contains("\"identifier\":{\"name\":\"Test ID\"", json);
        }

        #endregion

        #region Signin Tests

        [Fact]
        public void Signin_ShouldSerializeAndDeserializeCorrectly() {
            // Arrange
            var signin = new Signin(
                id: "signin-123",
                domain: "example.com",
                createdAt: 1705318200000,
                updatedAt: 1705318300000,
                identifier: new PortIdentifierSimple(prefix: "EIDPKZjEBB2yh", name: "User ID"),
                autoSignin: false
            );

            // Act
            var json = JsonSerializer.Serialize(signin, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<Signin>(json, _jsonOptions);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(signin.Id, deserialized.Id);
            Assert.Equal(signin.Domain, deserialized.Domain);
            Assert.Equal(signin.CreatedAt, deserialized.CreatedAt);
            Assert.Equal(signin.UpdatedAt, deserialized.UpdatedAt);
            Assert.Equal(signin.AutoSignin, deserialized.AutoSignin);
            Assert.NotNull(deserialized.Identifier);
            Assert.Equal("User ID", deserialized.Identifier.Name);
        }

        #endregion

        #region PortApprovedSignRequest Tests

        [Fact]
        public void PortApprovedSignRequest_ShouldSerializeCorrectly() {
            // Arrange
            var request = new PortApprovedSignRequest(
                originStr: "https://example.com",
                url: "https://api.example.com/data",
                method: "POST",
                selectedName: "My Identity",
                initHeadersDict: new Dictionary<string, string> {
                    ["Content-Type"] = "application/json",
                    ["Accept"] = "application/json"
                }
            );

            // Act
            var json = JsonSerializer.Serialize(request, _jsonOptions);

            // Assert
            Assert.Contains("\"originStr\":\"https://example.com\"", json);
            Assert.Contains("\"url\":\"https://api.example.com/data\"", json);
            Assert.Contains("\"method\":\"POST\"", json);
            Assert.Contains("\"selectedName\":\"My Identity\"", json);
            Assert.Contains("\"initHeadersDict\":{", json);
            Assert.Contains("\"Content-Type\":\"application/json\"", json);
        }

        [Fact]
        public void PortApprovedSignRequest_WithoutHeaders_ShouldSerializeCorrectly() {
            // Arrange
            var request = new PortApprovedSignRequest(
                originStr: "https://example.com",
                url: "https://api.example.com/data",
                method: "GET",
                selectedName: "My Identity"
            );

            // Act
            var json = JsonSerializer.Serialize(request, _jsonOptions);

            // Assert
            Assert.Contains("\"method\":\"GET\"", json);
            Assert.DoesNotContain("\"initHeadersDict\"", json);
        }

        #endregion

        #region Message Type Constants Tests

        [Fact]
        public void CsBwMsgTypes_ShouldHaveCorrectValues() {
            // Assert - verify constants match TypeScript values
            Assert.Equal("signify-extension", CsBwMsgTypes.POLARIS_SIGNIFY_EXTENSION);
            Assert.Equal("signify-extension-client", CsBwMsgTypes.POLARIS_SIGNIFY_EXTENSION_CLIENT);
            Assert.Equal("/signify/configure-vendor", CsBwMsgTypes.POLARIS_CONFIGURE_VENDOR);
            Assert.Equal("/signify/authorize", CsBwMsgTypes.POLARIS_SIGNIFY_AUTHORIZE);
            Assert.Equal("/signify/authorize/aid", CsBwMsgTypes.POLARIS_SELECT_AUTHORIZE_AID);
            Assert.Equal("/signify/authorize/credential", CsBwMsgTypes.POLARIS_SELECT_AUTHORIZE_CREDENTIAL);
            Assert.Equal("/signify/sign-data", CsBwMsgTypes.POLARIS_SIGN_DATA);
            Assert.Equal("/signify/sign-request", CsBwMsgTypes.POLARIS_SIGN_REQUEST);
            Assert.Equal("/signify/get-session-info", CsBwMsgTypes.POLARIS_GET_SESSION_INFO);
            Assert.Equal("/signify/clear-session", CsBwMsgTypes.POLARIS_CLEAR_SESSION);
            Assert.Equal("/signify/credential/create/data-attestation", CsBwMsgTypes.POLARIS_CREATE_DATA_ATTESTATION);
            Assert.Equal("/signify/credential/get", CsBwMsgTypes.POLARIS_GET_CREDENTIAL);
            Assert.Equal("init", CsBwMsgTypes.INIT);
        }

        [Fact]
        public void BwCsMsgTypes_ShouldHaveCorrectValues() {
            // Assert - verify constants match TypeScript values
            Assert.Equal("ready", BwCsMsgTypes.READY);
            Assert.Equal("reply_canceled", BwCsMsgTypes.REPLY_CANCELED);
            Assert.Equal("/signify/reply", BwCsMsgTypes.REPLY);
            Assert.Equal("fromBackgroundWorker", BwCsMsgTypes.FSW);
            Assert.Equal("app_closed", BwCsMsgTypes.APP_CLOSED);
            Assert.Equal("/KeriAuth/signify/replyCredential", BwCsMsgTypes.REPLY_CRED);
        }

        #endregion

        #region Round-trip Serialization Tests

        [Fact]
        public void ComplexMessage_ShouldSurviveRoundTrip() {
            // Arrange
            var original = new BwCsMsg(
                type: BwCsMsgTypes.REPLY,
                requestId: "complex-123",
                payload: new {
                    identifier = new PortIdentifier("EIDPKZjEBB2yh", "Test ID"),
                    credentials = new[] { 
                        new { 
                            id = "cred1", 
                            type = "vLEI" 
                        } 
                    },
                    nested = new {
                        deep = new {
                            value = 42
                        }
                    }
                }
            );

            // Act - serialize and deserialize
            var json = JsonSerializer.Serialize(original, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<BwCsMsg>(json, _jsonOptions);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Type, deserialized.Type);
            Assert.Equal(original.RequestId, deserialized.RequestId);
            Assert.NotNull(deserialized.Payload);
            
            // Verify JSON structure matches TypeScript expectations
            Assert.Contains("\"type\":\"/signify/reply\"", json);
            Assert.Contains("\"requestId\":\"complex-123\"", json);
            Assert.Contains("\"payload\":", json);
        }

        #endregion
    }
}