using Extension.Models;
using Extension.Utilities;

namespace Extension.Tests.Utilities;

public class KeriaConnectionDigestHelperTests {
    [Fact]
    public void Compute_ValidConfig_ReturnsHexDigest() {
        // Arrange
        var config = new KeriaConnectConfig(
            providerName: "Test Provider",
            adminUrl: "https://keria.example.com",
            bootUrl: "https://boot.example.com",
            passcodeHash: 12345,
            clientAidPrefix: "EClientPrefix123",
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        // Act
        var result = KeriaConnectionDigestHelper.Compute(config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(64, result.Value.Length); // SHA256 hex = 64 chars
        Assert.True(result.Value.All(c => char.IsAsciiHexDigitLower(c)));
    }

    [Fact]
    public void Compute_MissingClientAidPrefix_ReturnsFail() {
        // Arrange
        var config = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 12345,
            clientAidPrefix: null, // Missing
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        // Act
        var result = KeriaConnectionDigestHelper.Compute(config);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("ClientAidPrefix", result.Errors[0].Message);
    }

    [Fact]
    public void Compute_EmptyClientAidPrefix_ReturnsFail() {
        // Arrange
        var config = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 12345,
            clientAidPrefix: "   ", // Whitespace only
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        // Act
        var result = KeriaConnectionDigestHelper.Compute(config);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("ClientAidPrefix", result.Errors[0].Message);
    }

    [Fact]
    public void Compute_MissingAgentAidPrefix_ReturnsFail() {
        // Arrange
        var config = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 12345,
            clientAidPrefix: "EClientPrefix123",
            agentAidPrefix: null, // Missing
            isStored: true
        );

        // Act
        var result = KeriaConnectionDigestHelper.Compute(config);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("AgentAidPrefix", result.Errors[0].Message);
    }

    [Fact]
    public void Compute_ZeroPasscodeHash_ReturnsFail() {
        // Arrange
        var config = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 0, // Zero = not set
            clientAidPrefix: "EClientPrefix123",
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        // Act
        var result = KeriaConnectionDigestHelper.Compute(config);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("PasscodeHash", result.Errors[0].Message);
    }

    [Fact]
    public void Compute_SameInputs_ReturnsSameDigest() {
        // Arrange
        var config1 = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 12345,
            clientAidPrefix: "EClientPrefix123",
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        var config2 = new KeriaConnectConfig(
            providerName: "Different Name", // Name doesn't affect digest
            adminUrl: "https://different.url.com", // URL doesn't affect digest
            bootUrl: "https://boot.url.com", // Boot URL doesn't affect digest
            passcodeHash: 12345, // Same
            clientAidPrefix: "EClientPrefix123", // Same
            agentAidPrefix: "EAgentPrefix456", // Same
            isStored: false // IsStored doesn't affect digest
        );

        // Act
        var result1 = KeriaConnectionDigestHelper.Compute(config1);
        var result2 = KeriaConnectionDigestHelper.Compute(config2);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(result1.Value, result2.Value);
    }

    [Fact]
    public void Compute_DifferentInputs_ReturnsDifferentDigest() {
        // Arrange
        var config1 = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 12345,
            clientAidPrefix: "EClientPrefix123",
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        var config2 = new KeriaConnectConfig(
            providerName: "Test",
            adminUrl: "https://keria.example.com",
            bootUrl: null,
            passcodeHash: 12346, // Different passcode hash
            clientAidPrefix: "EClientPrefix123",
            agentAidPrefix: "EAgentPrefix456",
            isStored: true
        );

        // Act
        var result1 = KeriaConnectionDigestHelper.Compute(config1);
        var result2 = KeriaConnectionDigestHelper.Compute(config2);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotEqual(result1.Value, result2.Value);
    }
}
