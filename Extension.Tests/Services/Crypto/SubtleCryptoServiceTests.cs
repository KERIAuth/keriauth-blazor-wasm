using System.Security.Cryptography;
using System.Text;
using Extension.Services.Crypto;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace Extension.Tests.Services.Crypto;

public class SubtleCryptoServiceTests {
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<ILogger<SubtleCryptoService>> _mockLogger;
    private readonly SubtleCryptoService _service;

    public SubtleCryptoServiceTests() {
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockLogger = new Mock<ILogger<SubtleCryptoService>>();
        _service = new SubtleCryptoService(_mockJsRuntime.Object, _mockLogger.Object);
    }

    #region Sha256 Tests

    [Fact]
    public void Sha256_ShouldReturnCorrectHash_ForEmptyInput() {
        // Arrange
        var input = Array.Empty<byte>();
        // SHA256 of empty string is well-known
        var expectedHex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var expected = Convert.FromHexString(expectedHex);

        // Act
        var result = _service.Sha256(input);

        // Assert
        Assert.Equal(32, result.Length);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sha256_ShouldReturnCorrectHash_ForKnownInput() {
        // Arrange
        var input = Encoding.UTF8.GetBytes("hello world");
        // SHA256("hello world") is well-known
        var expectedHex = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";
        var expected = Convert.FromHexString(expectedHex);

        // Act
        var result = _service.Sha256(input);

        // Assert
        Assert.Equal(32, result.Length);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sha256_ShouldReturn32Bytes() {
        // Arrange
        var input = Encoding.UTF8.GetBytes("test data");

        // Act
        var result = _service.Sha256(input);

        // Assert
        Assert.Equal(32, result.Length);
    }

    [Fact]
    public void Sha256_ShouldReturnConsistentResults() {
        // Arrange
        var input = Encoding.UTF8.GetBytes("consistent data");

        // Act
        var result1 = _service.Sha256(input);
        var result2 = _service.Sha256(input);

        // Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Sha256_ShouldReturnDifferentResults_ForDifferentInputs() {
        // Arrange
        var input1 = Encoding.UTF8.GetBytes("data1");
        var input2 = Encoding.UTF8.GetBytes("data2");

        // Act
        var result1 = _service.Sha256(input1);
        var result2 = _service.Sha256(input2);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    #endregion

    #region DeriveKeyFromPrf Tests

    [Fact]
    public void DeriveKeyFromPrf_ShouldReturn32Bytes() {
        // Arrange
        var profileId = "test-profile-id";
        var prfOutput = new byte[32]; // 32 bytes of zeros

        // Act
        var result = _service.DeriveKeyFromPrf(profileId, prfOutput);

        // Assert
        Assert.Equal(32, result.Length);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldReturnConsistentResults() {
        // Arrange
        var profileId = "test-profile-id";
        var prfOutput = RandomNumberGenerator.GetBytes(32);

        // Act
        var result1 = _service.DeriveKeyFromPrf(profileId, prfOutput);
        var result2 = _service.DeriveKeyFromPrf(profileId, prfOutput);

        // Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldProduceDifferentKeys_ForDifferentProfileIds() {
        // Arrange
        var profileId1 = "profile-1";
        var profileId2 = "profile-2";
        var prfOutput = RandomNumberGenerator.GetBytes(32);

        // Act
        var result1 = _service.DeriveKeyFromPrf(profileId1, prfOutput);
        var result2 = _service.DeriveKeyFromPrf(profileId2, prfOutput);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldProduceDifferentKeys_ForDifferentPrfOutputs() {
        // Arrange
        var profileId = "test-profile";
        var prfOutput1 = RandomNumberGenerator.GetBytes(32);
        var prfOutput2 = RandomNumberGenerator.GetBytes(32);

        // Act
        var result1 = _service.DeriveKeyFromPrf(profileId, prfOutput1);
        var result2 = _service.DeriveKeyFromPrf(profileId, prfOutput2);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldMatchExpectedFormula() {
        // Arrange - test that the formula is SHA256(profileId || prfOutput || "KERI Auth")
        var profileId = "test-profile";
        var prfOutput = Encoding.UTF8.GetBytes("12345678901234567890123456789012"); // 32 bytes

        // Manually compute expected result
        var profileIdBytes = Encoding.UTF8.GetBytes(profileId);
        var labelBytes = Encoding.UTF8.GetBytes("KERI Auth");
        var combined = new byte[profileIdBytes.Length + prfOutput.Length + labelBytes.Length];
        Buffer.BlockCopy(profileIdBytes, 0, combined, 0, profileIdBytes.Length);
        Buffer.BlockCopy(prfOutput, 0, combined, profileIdBytes.Length, prfOutput.Length);
        Buffer.BlockCopy(labelBytes, 0, combined, profileIdBytes.Length + prfOutput.Length, labelBytes.Length);
        var expected = SHA256.HashData(combined);

        // Act
        var result = _service.DeriveKeyFromPrf(profileId, prfOutput);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldHandleUuidProfileId() {
        // Arrange - typical UUID format used in actual profile IDs
        var profileId = "550e8400-e29b-41d4-a716-446655440000";
        var prfOutput = RandomNumberGenerator.GetBytes(32);

        // Act
        var result = _service.DeriveKeyFromPrf(profileId, prfOutput);

        // Assert
        Assert.Equal(32, result.Length);
        Assert.NotNull(result);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldHandleEmptyProfileId() {
        // Arrange
        var profileId = "";
        var prfOutput = new byte[32];

        // Act
        var result = _service.DeriveKeyFromPrf(profileId, prfOutput);

        // Assert
        Assert.Equal(32, result.Length);
    }

    [Fact]
    public void DeriveKeyFromPrf_ShouldHandleNonStandardPrfOutputLength() {
        // Arrange - PRF output might not always be exactly 32 bytes
        var profileId = "test-profile";
        var prfOutput = new byte[64]; // Larger than standard

        // Act
        var result = _service.DeriveKeyFromPrf(profileId, prfOutput);

        // Assert
        Assert.Equal(32, result.Length);
    }

    #endregion

    #region GetRandomBytes Tests

    [Fact]
    public void GetRandomBytes_ShouldReturnRequestedLength() {
        // Arrange
        var length = 16;

        // Act
        var result = _service.GetRandomBytes(length);

        // Assert
        Assert.Equal(length, result.Length);
    }

    [Fact]
    public void GetRandomBytes_ShouldReturnDifferentResults_OnConsecutiveCalls() {
        // Arrange
        var length = 32;

        // Act
        var result1 = _service.GetRandomBytes(length);
        var result2 = _service.GetRandomBytes(length);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void GetRandomBytes_ShouldHandleZeroLength() {
        // Arrange
        var length = 0;

        // Act
        var result = _service.GetRandomBytes(length);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetRandomBytes_ShouldHandleLargeLength() {
        // Arrange
        var length = 1024;

        // Act
        var result = _service.GetRandomBytes(length);

        // Assert
        Assert.Equal(length, result.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void GetRandomBytes_ShouldReturnCorrectLength_ForVariousSizes(int length) {
        // Act
        var result = _service.GetRandomBytes(length);

        // Assert
        Assert.Equal(length, result.Length);
    }

    #endregion

    #region AES-GCM Parameter Validation Tests

    [Fact]
    public async Task AesGcmEncryptAsync_ShouldThrow_WhenKeyLengthIsNot32() {
        // Arrange
        var invalidKey = new byte[16]; // Should be 32
        var plaintext = Encoding.UTF8.GetBytes("test");
        var nonce = new byte[12];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AesGcmEncryptAsync(invalidKey, plaintext, nonce));
        Assert.Contains("32 bytes", exception.Message);
    }

    [Fact]
    public async Task AesGcmEncryptAsync_ShouldThrow_WhenNonceLengthIsNot12() {
        // Arrange
        var key = new byte[32];
        var plaintext = Encoding.UTF8.GetBytes("test");
        var invalidNonce = new byte[16]; // Should be 12

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AesGcmEncryptAsync(key, plaintext, invalidNonce));
        Assert.Contains("12 bytes", exception.Message);
    }

    [Fact]
    public async Task AesGcmDecryptAsync_ShouldThrow_WhenKeyLengthIsNot32() {
        // Arrange
        var invalidKey = new byte[24]; // Should be 32
        var ciphertext = new byte[32];
        var nonce = new byte[12];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AesGcmDecryptAsync(invalidKey, ciphertext, nonce));
        Assert.Contains("32 bytes", exception.Message);
    }

    [Fact]
    public async Task AesGcmDecryptAsync_ShouldThrow_WhenNonceLengthIsNot12() {
        // Arrange
        var key = new byte[32];
        var ciphertext = new byte[32];
        var invalidNonce = new byte[8]; // Should be 12

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AesGcmDecryptAsync(key, ciphertext, invalidNonce));
        Assert.Contains("12 bytes", exception.Message);
    }

    #endregion
}
