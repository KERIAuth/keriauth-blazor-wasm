using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Helper;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;

namespace Extension.Tests.Models {
    /// <summary>
    /// Tests for round-trip message serialization/deserialization.
    /// These tests verify that messages can be:
    /// 1. Serialized to JSON
    /// 2. Deserialized to a generic base type (for inspection)
    /// 3. Re-deserialized to a specific typed message
    ///
    /// This simulates the two-phase deserialization pattern used in:
    /// - BackgroundWorker: receives JSON → deserializes to ToBwMessage/AppBwMessage → routes by Type
    /// - AppBwMessagingService: receives JSON → deserializes to FromBwMessage → notifies subscribers
    /// </summary>
    public class MessageRoundTripTests {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly JsonSerializerOptions _recursiveDictOptions;

        public MessageRoundTripTests() {
            _jsonOptions = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            _recursiveDictOptions = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                MaxDepth = 128,
                Converters = { new RecursiveDictionaryConverter() }
            };
        }

        #region ToBwMessage Round-Trip Tests

        [Fact]
        public void ToBwMessage_RoundTrip_WithNestedPayload() {
            // Arrange: Create a message with deeply nested payload (simulating a KERI credential structure)
            var nestedPayload = new Dictionary<string, object> {
                ["v"] = "ACDC10JSON000197_",
                ["d"] = "EHMnCf8_nIemuPx-cUHb1k5DsT8K09vqx0bSwNRr9S4c",
                ["i"] = "EEXekkGu9IAzav6pZVJhkLnjtjM5v3AcyA-pdKUcaGei",
                ["s"] = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao",
                ["a"] = new Dictionary<string, object> {
                    ["d"] = "EBQ7tI5qA6-SyzxdkI4P3eBEH4j7BB7Nj59RbYw3xVpY",
                    ["dt"] = "2024-01-15T12:00:00.000000+00:00",
                    ["i"] = "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c"
                }
            };

            var originalMessage = new AppBwMessage<object>(
                type: AppBwMessageType.ReplyCredential,
                tabId: 42,
                tabUrl: "https://example.com",
                requestId: "req-12345",
                payload: nestedPayload
            );

            // Act 1: Serialize to JSON (simulates message being sent)
            var json = JsonSerializer.Serialize(originalMessage, _jsonOptions);

            // Act 2: Deserialize to non-generic AppBwMessage (two-phase: first step)
            var baseMessage = JsonSerializer.Deserialize<AppBwMessage>(json, _jsonOptions);

            // Assert: Base message properties preserved
            Assert.NotNull(baseMessage);
            Assert.Equal(AppBwMessageType.ReplyCredential.Value, baseMessage.Type);
            Assert.Equal(42, baseMessage.TabId);
            Assert.Equal("https://example.com", baseMessage.TabUrl);
            Assert.Equal("req-12345", baseMessage.RequestId);
            Assert.True(baseMessage.Payload.HasValue);
            Assert.Equal(JsonValueKind.Object, baseMessage.Payload.Value.ValueKind);

            // Act 3: Convert to typed message with RecursiveDictionary payload (two-phase: second step)
            // Note: Using RecursiveDictionary explicitly as the type parameter ensures
            // field ordering is preserved for CESR/SAID. Using object would give JsonElement.
            var typedMessage = baseMessage.ToTyped<RecursiveDictionary>(_recursiveDictOptions);

            // Assert: Typed message properties preserved
            Assert.NotNull(typedMessage);
            Assert.Equal(AppBwMessageType.ReplyCredential.Value, typedMessage.Type);
            Assert.Equal(42, typedMessage.TabId);
            Assert.Equal("https://example.com", typedMessage.TabUrl);
            Assert.NotNull(typedMessage.Payload);

            // The payload is now explicitly RecursiveDictionary (preserves ordering for CESR/SAID)
            var payloadDict = typedMessage.Payload;

            // Assert: Nested values are accessible and correct via RecursiveValue
            Assert.Equal("ACDC10JSON000197_", ((RecursiveValue)payloadDict["v"]).StringValue);
            Assert.Equal("EHMnCf8_nIemuPx-cUHb1k5DsT8K09vqx0bSwNRr9S4c", ((RecursiveValue)payloadDict["d"]).StringValue);

            // Assert: Nested object is accessed via RecursiveValue.Dictionary
            var aValue = payloadDict["a"];
            Assert.IsType<RecursiveValue>(aValue);
            var nestedDict = ((RecursiveValue)aValue).Dictionary;
            Assert.NotNull(nestedDict);
            Assert.Equal("EBQ7tI5qA6-SyzxdkI4P3eBEH4j7BB7Nj59RbYw3xVpY", ((RecursiveValue)nestedDict["d"]).StringValue);
        }

        [Fact]
        public void ToBwMessage_RoundTrip_NullPayload() {
            // Arrange: Message with null payload
            var originalMessage = new AppBwMessage<object>(
                type: AppBwMessageType.UserActivity,
                tabId: 1,
                tabUrl: null,
                requestId: null,
                payload: null
            );

            // Act 1: Serialize to JSON
            var json = JsonSerializer.Serialize(originalMessage, _jsonOptions);

            // Act 2: Deserialize to non-generic base
            var baseMessage = JsonSerializer.Deserialize<AppBwMessage>(json, _jsonOptions);

            // Assert
            Assert.NotNull(baseMessage);
            Assert.Equal(AppBwMessageType.UserActivity.Value, baseMessage.Type);
            Assert.False(baseMessage.Payload.HasValue || baseMessage.Payload?.ValueKind == JsonValueKind.Null);

            // Act 3: Convert to typed
            var typedMessage = baseMessage.ToTyped<object>(_recursiveDictOptions);
            Assert.NotNull(typedMessage);
            Assert.Null(typedMessage.Payload);
        }

        #endregion

        #region FromBwMessage Round-Trip Tests

        [Fact]
        public void FromBwMessage_RoundTrip_WithNestedData() {
            // Arrange: Create a BwAppMessage with nested data
            var nestedData = new Dictionary<string, object> {
                ["identifier"] = new Dictionary<string, object> {
                    ["prefix"] = "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c",
                    ["name"] = "My Identifier"
                },
                ["session"] = new Dictionary<string, object> {
                    ["oneTime"] = false
                }
            };

            var originalMessage = new BwAppMessage<object>(
                type: BwAppMessageType.RequestSelectAuthorize,
                requestId: "req-auth-001",
                data: nestedData
            );

            // Act 1: Serialize to JSON
            var json = JsonSerializer.Serialize(originalMessage, _jsonOptions);

            // Assert: JSON uses "data" key (not "payload") per polaris-web protocol
            Assert.Contains("\"data\":", json);
            Assert.DoesNotContain("\"payload\":", json);

            // Act 2: Deserialize to non-generic FromBwMessage
            var baseMessage = JsonSerializer.Deserialize<FromBwMessage>(json, _jsonOptions);

            // Assert: Base message properties preserved
            Assert.NotNull(baseMessage);
            Assert.Equal(BwAppMessageType.RequestSelectAuthorize.Value, baseMessage.Type);
            Assert.Equal("req-auth-001", baseMessage.RequestId);
            Assert.True(baseMessage.Data.HasValue);
            Assert.Equal(JsonValueKind.Object, baseMessage.Data.Value.ValueKind);

            // Act 3: Deserialize Data to specific type
            var dataDict = JsonSerializer.Deserialize<RecursiveDictionary>(
                baseMessage.Data.Value.GetRawText(),
                _recursiveDictOptions);

            // Assert: Nested values accessible via RecursiveValue
            Assert.NotNull(dataDict);
            Assert.True(dataDict.ContainsKey("identifier"));
            var identifierValue = (RecursiveValue)dataDict["identifier"];
            Assert.NotNull(identifierValue.Dictionary);
            var prefixValue = (RecursiveValue)identifierValue.Dictionary["prefix"];
            Assert.Equal("EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c", prefixValue.StringValue);
        }

        [Fact]
        public void FromBwMessage_RoundTrip_WithError() {
            // Arrange: Create message with error
            var originalMessage = new BwAppMessage<object>(
                type: BwAppMessageType.ForwardedMessage,
                requestId: "req-error-001",
                data: null,
                error: "Something went wrong"
            );

            // Act 1: Serialize
            var json = JsonSerializer.Serialize(originalMessage, _jsonOptions);

            // Act 2: Deserialize to base
            var baseMessage = JsonSerializer.Deserialize<FromBwMessage>(json, _jsonOptions);

            // Assert
            Assert.NotNull(baseMessage);
            Assert.Equal("Something went wrong", baseMessage.Error);
            Assert.False(baseMessage.Data.HasValue);
        }

        #endregion

        #region AppBwMessageType TryParse Tests

        [Fact]
        public void AppBwMessageType_TryParse_ValidType_ReturnsTrue() {
            // Act
            var result = AppBwMessageType.TryParse(AppBwMessageType.Values.ReplyCredential, out var msgType);

            // Assert
            Assert.True(result);
            Assert.Equal(AppBwMessageType.ReplyCredential, msgType);
        }

        [Fact]
        public void AppBwMessageType_TryParse_InvalidType_ReturnsFalse() {
            // Act
            var result = AppBwMessageType.TryParse("Invalid.Type", out var msgType);

            // Assert
            Assert.False(result);
            Assert.Equal(default, msgType);
        }

        [Fact]
        public void AppBwMessageType_TryParse_NullType_ReturnsFalse() {
            // Act
            var result = AppBwMessageType.TryParse(null, out var msgType);

            // Assert
            Assert.False(result);
            Assert.Equal(default, msgType);
        }

        #endregion

        #region BwAppMessageType TryParse Tests

        [Fact]
        public void BwAppMessageType_TryParse_ValidType_ReturnsTrue() {
            // Act
            var result = BwAppMessageType.TryParse(BwAppMessageType.Values.LockApp, out var msgType);

            // Assert
            Assert.True(result);
            Assert.Equal(BwAppMessageType.LockApp, msgType);
        }

        [Fact]
        public void BwAppMessageType_TryParse_InvalidType_ReturnsFalse() {
            // Act
            var result = BwAppMessageType.TryParse("Invalid.Type", out var msgType);

            // Assert
            Assert.False(result);
            Assert.Equal(default, msgType);
        }

        #endregion

        #region Full Simulation Tests

        [Fact]
        public void FullSimulation_AppToBackgroundWorker_MessageRouting() {
            // This test simulates the full message flow from App to BackgroundWorker

            // Arrange: App creates and sends a message
            var appMessage = new AppBwMessage<object>(
                type: AppBwMessageType.ReplyApprovedSignHeaders,
                tabId: 100,
                tabUrl: "https://secure.example.com",
                requestId: "sign-headers-001",
                payload: new Dictionary<string, object> {
                    ["headersDict"] = new Dictionary<string, string> {
                        ["signature"] = "test-sig-value",
                        ["signature-input"] = "test-input"
                    },
                    ["prefix"] = "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c",
                    ["isApproved"] = true
                }
            );

            var json = JsonSerializer.Serialize(appMessage, _jsonOptions);

            // Act: BackgroundWorker receives and processes
            // Step 1: Deserialize to non-generic to check type
            var baseMsg = JsonSerializer.Deserialize<AppBwMessage>(json, _jsonOptions);
            Assert.NotNull(baseMsg);

            // Step 2: Validate type is known
            Assert.True(AppBwMessageType.TryParse(baseMsg.Type, out var msgType));
            Assert.Equal(AppBwMessageType.ReplyApprovedSignHeaders, msgType);

            // Step 3: Route by type - convert to typed message with RecursiveDictionary
            // Note: Use RecursiveDictionary explicitly when payload contains CESR/SAID data
            var typedMsg = baseMsg.ToTyped<RecursiveDictionary>(_recursiveDictOptions);

            // Assert: All properties preserved correctly
            Assert.Equal(100, typedMsg.TabId);
            Assert.Equal("https://secure.example.com", typedMsg.TabUrl);
            Assert.Equal("sign-headers-001", typedMsg.RequestId);
            Assert.NotNull(typedMsg.Payload);

            var payload = typedMsg.Payload;
            var prefixValue = (RecursiveValue)payload["prefix"];
            Assert.Equal("EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c", prefixValue.StringValue);
            var isApprovedValue = (RecursiveValue)payload["isApproved"];
            Assert.True(isApprovedValue.BooleanValue);
        }

        [Fact]
        public void FullSimulation_BackgroundWorkerToApp_MessageRouting() {
            // This test simulates the full message flow from BackgroundWorker to App

            // Arrange: BackgroundWorker creates and sends a message
            var bwMessage = new BwAppMessage<object>(
                type: BwAppMessageType.RequestSignHeaders,
                requestId: "sign-req-001",
                data: new Dictionary<string, object> {
                    ["url"] = "https://api.example.com/resource",
                    ["method"] = "POST",
                    ["headers"] = new Dictionary<string, string> {
                        ["content-type"] = "application/json"
                    }
                }
            );

            var json = JsonSerializer.Serialize(bwMessage, _jsonOptions);

            // Act: App receives and processes (simulating AppBwMessagingService)
            // Step 1: Deserialize to non-generic FromBwMessage
            var baseMsg = JsonSerializer.Deserialize<FromBwMessage>(json, _jsonOptions);
            Assert.NotNull(baseMsg);

            // Step 2: Validate type
            Assert.True(BwAppMessageType.TryParse(baseMsg.Type, out var msgType));
            Assert.Equal(BwAppMessageType.RequestSignHeaders, msgType);

            // Step 3: Convert Data to RecursiveDictionary for BwAppMessage
            // Note: Use RecursiveDictionary explicitly when data contains CESR/SAID structures
            RecursiveDictionary? data = baseMsg.Data.HasValue
                ? JsonSerializer.Deserialize<RecursiveDictionary>(baseMsg.Data.Value.GetRawText(), _recursiveDictOptions)
                : null;

            var appReceivedMsg = new BwAppMessage(baseMsg.Type, baseMsg.RequestId, data, baseMsg.Error);

            // Assert: Properties preserved
            Assert.Equal(BwAppMessageType.RequestSignHeaders.Value, appReceivedMsg.Type);
            Assert.Equal("sign-req-001", appReceivedMsg.RequestId);
            Assert.NotNull(appReceivedMsg.Data);
            Assert.IsType<RecursiveDictionary>(appReceivedMsg.Data);

            var dataDict = (RecursiveDictionary)appReceivedMsg.Data;
            var urlValue = (RecursiveValue)dataDict["url"];
            var methodValue = (RecursiveValue)dataDict["method"];
            Assert.Equal("https://api.example.com/resource", urlValue.StringValue);
            Assert.Equal("POST", methodValue.StringValue);
        }

        #endregion
    }
}
