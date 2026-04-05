namespace Extension.Tests.Models;

using Extension.Models;
using System.Text.Json;
using Xunit;

/// <summary>
/// Tests for KeriaConnectConfig with nested Connections and Passkeys.
/// </summary>
public class KeriaConnectConfigTests {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void KeriaConnectConfig_DefaultsToEmptyCollections() {
        var config = new KeriaConnectConfig();
        Assert.Empty(config.Connections);
        Assert.Empty(config.Passkeys);
    }

    [Fact]
    public void KeriaConnectConfig_WithConnections_SerializationRoundTrip() {
        var config = new KeriaConnectConfig {
            Alias = "test",
            Connections = [
                new Connection {
                    Name = "conn1",
                    SenderPrefix = "sender1",
                    ReceiverPrefix = "receiver1",
                    ConnectionDate = DateTime.UtcNow
                }
            ]
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<KeriaConnectConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Connections);
        Assert.Equal("conn1", deserialized.Connections[0].Name);
    }

    [Fact]
    public void KeriaConnectConfig_WithPasskeys_SerializationRoundTrip() {
        var config = new KeriaConnectConfig {
            Passkeys = [
                new StoredPasskey {
                    SchemaVersion = StoredPasskeySchema.CurrentVersion,
                    CredentialBase64 = "cred1",
                    Transports = ["internal"],
                    EncryptedPasscodeBase64 = "enc",
                    KeriaConnectionDigest = "digest",
                    Aaguid = "00000000-0000-0000-0000-000000000000",
                    CreationTime = DateTime.UtcNow
                }
            ]
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<KeriaConnectConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Passkeys);
        Assert.Equal("cred1", deserialized.Passkeys[0].CredentialBase64);
    }

    [Fact]
    public void KeriaConnectConfigs_WithNestedData_SerializationRoundTrip() {
        var configs = new KeriaConnectConfigs {
            Configs = new Dictionary<string, KeriaConnectConfig> {
                ["digest1"] = new KeriaConnectConfig {
                    Alias = "Config 1",
                    Connections = [new Connection { Name = "c1", SenderPrefix = "s", ReceiverPrefix = "r", ConnectionDate = DateTime.UtcNow }],
                    Passkeys = [new StoredPasskey {
                        SchemaVersion = StoredPasskeySchema.CurrentVersion,
                        CredentialBase64 = "pk1", Transports = ["internal"],
                        EncryptedPasscodeBase64 = "e", KeriaConnectionDigest = "digest1",
                        Aaguid = "00000000-0000-0000-0000-000000000000", CreationTime = DateTime.UtcNow
                    }]
                }
            }
        };

        var json = JsonSerializer.Serialize(configs, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<KeriaConnectConfigs>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Configs);
        Assert.Single(deserialized.Configs["digest1"].Connections);
        Assert.Single(deserialized.Configs["digest1"].Passkeys);
    }
}
