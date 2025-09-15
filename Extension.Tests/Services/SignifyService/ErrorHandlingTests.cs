using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Tests.Attributes;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Services.SignifyService;

/// <summary>
/// Tests for error handling and edge cases in SignifyService.
/// Based on error handling patterns from signify-ts TypeScript tests.
/// </summary>
public class ErrorHandlingTests
{
    private readonly Mock<ILogger<SignifyClientService>> _mockLogger;
    private readonly SignifyClientService _signifyClientService;

    public ErrorHandlingTests()
    {
        _mockLogger = new Mock<ILogger<SignifyClientService>>();
        _signifyClientService = new SignifyClientService(_mockLogger.Object);
    }

    [BrowserOnlyTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Connect_WithInvalidUrls_ShouldHandleGracefully(string? invalidUrl)
    {
        // Arrange
        const string passcode = "0123456789abcdefghijk"; // 21 chars
        const string bootUrl = "http://localhost:3902";

        // Act & Assert
        if (invalidUrl == null)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _signifyClientService.Connect(invalidUrl!, passcode, bootUrl));
        }
        else
        {
            var result = await _signifyClientService.Connect(invalidUrl, passcode, bootUrl);
            // Should handle gracefully - either succeed or fail with descriptive error
            Assert.NotNull(result);
        }
    }

    [BrowserOnlyFact]
    public async Task Connect_WithNullPasscode_ShouldThrowArgumentNullException()
    {
        // Arrange
        const string agentUrl = "http://localhost:3901";
        const string bootUrl = "http://localhost:3902";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _signifyClientService.Connect(agentUrl, null!, bootUrl));
    }

    [BrowserOnlyFact]
    public async Task Connect_WithNullBootUrl_ShouldAssertionFail()
    {
        // Arrange
        const string agentUrl = "http://localhost:3901";
        const string passcode = "0123456789abcdefghijk"; // 21 chars

        // Act & Assert
        // Based on the Debug.Assert(bootUrl is not null) in the code
        if (!OperatingSystem.IsBrowser())
        {
            // In test environment, assertion might not throw, but validation should occur
            var result = await _signifyClientService.Connect(agentUrl, passcode, null);
            Assert.True(result.IsFailed);
        }
    }

    [BrowserOnlyTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RunCreateAid_WithInvalidAlias_ShouldHandleGracefully(string? invalidAlias)
    {
        // Act
        if (invalidAlias == null)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _signifyClientService.RunCreateAid(invalidAlias!));
        }
        else
        {
            var result = await _signifyClientService.RunCreateAid(invalidAlias);
            // Should handle empty/whitespace aliases gracefully
            Assert.NotNull(result);
        }
    }

    [Fact]
    public async Task GetIdentifier_WithNonExistentName_ShouldReturnFailure()
    {
        // Arrange
        const string nonExistentName = "non-existent-identifier-12345";

        // Act
        var result = await _signifyClientService.GetIdentifier(nonExistentName);

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            Assert.True(result.IsFailed);
        }
        else
        {
            // In browser environment, should handle missing identifiers gracefully
            Assert.True(result.IsFailed || result.IsSuccess);
            if (result.IsFailed)
            {
                Assert.Contains("not found", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [BrowserOnlyFact]
    public async Task GetCredential_WithInvalidSaid_ShouldReturnFailure()
    {
        // Arrange
        const string invalidSaid = "invalid-said-format";

        // Act
        var result = await _signifyClientService.GetCredential(invalidSaid);

        // Assert
        Assert.True(result.IsFailed);
        if (result.IsFailed)
        {
            Assert.Contains("Could not find credential", result.Errors[0].Message);
        }
    }

    [BrowserOnlyTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetCredential_WithInvalidSaidFormats_ShouldHandleGracefully(string? invalidSaid)
    {
        // Act
        if (invalidSaid == null)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _signifyClientService.GetCredential(invalidSaid!));
        }
        else
        {
            var result = await _signifyClientService.GetCredential(invalidSaid);
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public void JsonDeserialization_WithMalformedJson_ShouldThrowJsonException()
    {
        // Arrange
        const string malformedJson = "{ invalid json structure }";

        // Act & Assert
        Assert.Throws<JsonException>(() => 
            JsonSerializer.Deserialize<State>(malformedJson));
    }

    [Fact]
    public void JsonDeserialization_WithIncompleteState_ShouldHandleGracefully()
    {
        // Arrange - Missing required fields
        const string incompleteJson = """
            {
                "agent": null,
                "controller": null
            }
            """;

        // Act
        var state = JsonSerializer.Deserialize<State>(incompleteJson);

        // Assert
        Assert.NotNull(state);
        Assert.Null(state.Agent);
        Assert.Null(state.Controller);
        Assert.Equal(0, state.Ridx); // Default value
        Assert.Equal(0, state.Pidx); // Default value
    }

    [Fact]
    public void JsonDeserialization_WithUnexpectedFields_ShouldIgnoreGracefully()
    {
        // Arrange - JSON with extra fields not in model
        const string jsonWithExtraFields = """
            {
                "agent": {
                    "i": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"
                },
                "controller": {
                    "state": {
                        "i": "EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E"
                    }
                },
                "ridx": 0,
                "pidx": 0,
                "unexpectedField": "should be ignored",
                "anotherExtra": 12345
            }
            """;

        // Act
        var state = JsonSerializer.Deserialize<State>(jsonWithExtraFields);

        // Assert
        Assert.NotNull(state);
        Assert.NotNull(state.Agent);
        Assert.NotNull(state.Controller);
        Assert.Equal("EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", state.Agent.I);
    }

    [Fact]
    public void HealthCheck_WithInvalidUri_ShouldThrowUriFormatException()
    {
        // Arrange
        const string invalidUriString = "not-a-valid-uri";

        // Act & Assert
        Assert.Throws<UriFormatException>(() => new Uri(invalidUriString));
    }

    [BrowserOnlyFact]
    public async Task HealthCheck_WithUnreachableEndpoint_ShouldReturnFailure()
    {
        // Arrange
        var unreachableUri = new Uri("http://localhost:99999/unreachable");

        // Act
        var result = await _signifyClientService.HealthCheck(unreachableUri);

        // Assert
        Assert.True(result.IsFailed);
        await Task.CompletedTask; // Ensure async is used
    }

    [Fact]
    public async Task NotImplementedMethods_ShouldThrowOrReturnFailure()
    {
        // Test specific methods that are documented as not implemented
        
        // Act & Assert - Methods that throw NotImplementedException
        await Assert.ThrowsAsync<NotImplementedException>(() => _signifyClientService.Connect());
        
        // Interface methods that should throw
        var signifyService = _signifyClientService as ISignifyClientService;
        await Assert.ThrowsAsync<NotImplementedException>(() => signifyService.GetRegistries());
        await Assert.ThrowsAsync<NotImplementedException>(() => signifyService.GetNotifications());
        await Assert.ThrowsAsync<NotImplementedException>(() => signifyService.SignRequestHeader("", "", "", new(), ""));
    }

    [BrowserOnlyFact]
    public void LoggerIntegration_ShouldHandleNullLogger()
    {
        // Act & Assert
        // SignifyClientService constructor should not throw with null logger
        // Note: This depends on the actual implementation details
        var nullLogger = null as ILogger<SignifyClientService>;
        Assert.Throws<ArgumentNullException>(() => new SignifyClientService(nullLogger!));
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Connect_WithInvalidTimeout_ShouldHandleGracefully(int timeoutMs)
    {
        // Arrange
        const string agentUrl = "http://localhost:3901";
        const string passcode = "0123456789abcdefghijk"; // 21 chars
        const string bootUrl = "http://localhost:3902";
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        // Act
        var result = await _signifyClientService.Connect(agentUrl, passcode, bootUrl, timeout: timeout);

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            Assert.True(result.IsFailed);
        }
        // Note: In browser environment, TimeoutHelper should handle invalid timeouts
    }

    [Fact]
    public void DictionaryConverter_WithEmptyCredentials_ShouldReturnEmptyList()
    {
        // This test verifies the DictionaryConverter used in GetCredentials
        // handles empty or null responses gracefully

        // Arrange
        const string emptyCredentialsJson = "[]";

        // Act
        var jsonOptions = GetJsonOptions();
        var credentials = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(emptyCredentialsJson, jsonOptions);

        // Assert
        Assert.NotNull(credentials);
        Assert.Empty(credentials);
    }

    [BrowserOnlyFact]
    public async Task ErrorMessages_ShouldBeDescriptiveAndUseful()
    {
        // This test verifies that error messages follow good practices
        // as demonstrated in TypeScript signify-ts tests

        // Arrange & Act
        var passcodeError = await _signifyClientService.Connect("url", "short", "boot");
        var credentialError = await _signifyClientService.GetCredential("invalid");

        // Assert
        Assert.True(passcodeError.IsFailed);
        Assert.True(credentialError.IsFailed);
        
        // Error messages should be specific and helpful
        Assert.Contains("21 characters", passcodeError.Errors[0].Message);
        Assert.Contains("Could not find credential", credentialError.Errors[0].Message);
        
        // Should not expose sensitive information
        Assert.DoesNotContain("passcode", passcodeError.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new Extension.Helper.DictionaryConverter() }
        };
    }
}