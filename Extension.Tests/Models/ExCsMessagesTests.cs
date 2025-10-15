using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.CsBw;
using Extension.Models.Messages.ExCs;

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

        #region ContentScriptMessage Tests

        [Fact]
        public void ContentScriptMessage_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new CsBwMessage(
                type: CsBwMessageTypes.AUTHORIZE,
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
        public void ContentScriptMessage_ShouldDeserializeCorrectly() {
            // Arrange
            var json = "{\"type\":\"/signify/authorize\",\"requestId\":\"req-123\",\"payload\":{\"test\":\"data\"}}";

            // Act
            var msg = JsonSerializer.Deserialize<CsBwMessage>(json, _jsonOptions);

            // Assert
            Assert.NotNull(msg);
            Assert.Equal(CsBwMessageTypes.AUTHORIZE, msg.Type);
            Assert.Equal("req-123", msg.RequestId);
            Assert.NotNull(msg.Payload);
        }

        [Fact]
        public void ContentScriptMessage_WithNullOptionalFields_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new CsBwMessage(type: CsBwMessageTypes.INIT);

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Equal("{\"type\":\"init\"}", json);
        }

        #endregion

        #region AppMessage Tests

        [Fact]
        public void AppMessage_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new AppBwMessage(
                type: AppBwMessageTypes.REPLY_CREDENTIAL,
                tabId: 42,
                requestId: "req-456",
                payload: new { credential = "data" }
            );

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Contains("\"type\":\"/KeriAuth/signify/replyCredential\"", json);
            Assert.Contains("\"tabId\":42", json);
            Assert.Contains("\"requestId\":\"req-456\"", json);
            Assert.Contains("\"payload\":{\"credential\":\"data\"}", json);
        }

        [Fact]
        public void AppMessage_ShouldDeserializeCorrectly() {
            // Arrange
            var json = "{\"type\":\"/KeriAuth/signify/replyCredential\",\"tabId\":42,\"requestId\":\"req-456\",\"payload\":{\"credential\":\"data\"}}";

            // Act
            var msg = JsonSerializer.Deserialize<AppBwMessage>(json, _jsonOptions);

            // Assert
            Assert.NotNull(msg);
            Assert.Equal(AppBwMessageTypes.REPLY_CREDENTIAL, msg.Type);
            Assert.Equal(42, msg.TabId);
            Assert.Equal("req-456", msg.RequestId);
            Assert.NotNull(msg.Payload);
        }

        #endregion

        #region OutboundMessage Tests

        [Fact]
        public void OutboundMessage_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new BwCsMessage(
                type: BwCsMessageTypes.REPLY,
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
        public void OutboundMessage_WithError_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new BwCsMessage(
                type: BwCsMessageTypes.REPLY_CANCELED,
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
        public void ReadyMessage_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new ReadyMessage();

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Equal("{\"type\":\"ready\"}", json);
        }

        [Fact]
        public void ErrorReplyMessage_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new ErrorReplyMessage("req-789", "Something went wrong");

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Contains("\"type\":\"/signify/reply\"", json);
            Assert.Contains("\"requestId\":\"req-789\"", json);
            Assert.Contains("\"error\":\"Something went wrong\"", json);
        }

        [Fact]
        public void ReplyMessage_ShouldSerializeCorrectly() {
            // Arrange
            var msg = new ReplyMessage<Dictionary<string, string>>(
                "req-123",
                new Dictionary<string, string> {
                    ["key1"] = "value1",
                    ["key2"] = "value2"
                }
            );

            // Act
            var json = JsonSerializer.Serialize(msg, _jsonOptions);

            // Assert
            Assert.Contains("\"type\":\"/signify/reply\"", json);
            Assert.Contains("\"requestId\":\"req-123\"", json);
            Assert.Contains("\"payload\":{", json);
            Assert.Contains("\"key1\":\"value1\"", json);
        }

        #endregion
    }
}
