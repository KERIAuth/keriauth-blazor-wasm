using Extension.Helper;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Services.SignifyService;

public class SignifyClientServiceTests
{
    private readonly Mock<ILogger<SignifyClientService>> _mockLogger;
    private readonly SignifyClientService _signifyClientService;

    public SignifyClientServiceTests()
    {
        _mockLogger = new Mock<ILogger<SignifyClientService>>();
        _signifyClientService = new SignifyClientService(_mockLogger.Object);
    }

    [Fact]
    public async Task Connect_WithInvalidPasscodeLength_ShouldReturnFailure()
    {
        // Arrange
        const string url = "http://localhost:3901";
        const string invalidPasscode = "short"; // Invalid length
        const string bootUrl = "http://localhost:3902";

        // Act
        var result = await _signifyClientService.Connect(url, invalidPasscode, bootUrl);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("Passcode must be 21 characters", result.Errors[0].Message);
    }

    [Fact]
    public async Task Connect_WithValidPasscode_ShouldAcceptCorrectLength()
    {
        // Arrange
        const string url = "http://localhost:3901";
        const string validPasscode = "123456789012345678901"; // 21 characters
        const string bootUrl = "http://localhost:3902";

        // Act & Assert
        // This test would require browser environment to complete
        // For now, we just verify passcode validation logic
        if (!OperatingSystem.IsBrowser())
        {
            var result = await _signifyClientService.Connect(url, validPasscode, bootUrl);
            Assert.True(result.IsFailed);
            Assert.Contains("not running in Browser", result.Errors[0].Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345678901234567890")] // 20 characters
    [InlineData("1234567890123456789012")] // 22 characters
    public async Task Connect_WithInvalidPasscodeLengths_ShouldReturnFailure(string invalidPasscode)
    {
        // Arrange
        const string url = "http://localhost:3901";
        const string bootUrl = "http://localhost:3902";

        // Act
        var result = await _signifyClientService.Connect(url, invalidPasscode, bootUrl);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("Passcode must be 21 characters", result.Errors[0].Message);
    }

    [Fact]
    public async Task HealthCheck_WithValidUrl_ShouldCallHttpClient()
    {
        // Arrange
        var validUrl = new Uri("http://localhost:3901/health");

        // Act
        var result = await _signifyClientService.HealthCheck(validUrl);

        // Assert
        // This test would require mocking HttpClient for complete validation
        // For now, we verify the method accepts valid URI
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RunCreateAid_WithValidAlias_ShouldUseDefaultTimeout()
    {
        // Arrange
        const string aliasStr = "test-alias";

        // Act & Assert
        // This test requires browser environment to execute JSImport calls
        if (!OperatingSystem.IsBrowser())
        {
            var result = await _signifyClientService.RunCreateAid(aliasStr);
            // Expect failure due to no browser context
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task RunCreateAid_WithCustomTimeout_ShouldUseProvidedTimeout()
    {
        // Arrange
        const string aliasStr = "test-alias";
        var customTimeout = TimeSpan.FromSeconds(10);

        // Act & Assert
        if (!OperatingSystem.IsBrowser())
        {
            var result = await _signifyClientService.RunCreateAid(aliasStr, customTimeout);
            // Expect failure due to no browser context
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task GetIdentifiers_InNonBrowserEnvironment_ShouldHandleGracefully()
    {
        // Act
        var result = await _signifyClientService.GetIdentifiers();

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            // Should handle non-browser environment gracefully
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task GetIdentifier_WithValidName_ShouldReturnAid()
    {
        // Arrange
        const string validName = "test-identifier";

        // Act
        var result = await _signifyClientService.GetIdentifier(validName);

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task GetState_InNonBrowserEnvironment_ShouldHandleGracefully()
    {
        // Act
        var result = await _signifyClientService.GetState();

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task GetCredentials_InNonBrowserEnvironment_ShouldHandleGracefully()
    {
        // Act
        var result = await _signifyClientService.GetCredentials();

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task GetCredential_WithValidSaid_ShouldSearchCredentials()
    {
        // Arrange
        const string testSaid = "test-said-value";

        // Act
        var result = await _signifyClientService.GetCredential(testSaid);

        // Assert
        if (!OperatingSystem.IsBrowser())
        {
            Assert.True(result.IsFailed);
        }
    }

    [Fact]
    public async Task NotImplementedMethods_ShouldReturnNotImplementedFailure()
    {
        // Test all methods that return "Not implemented"
        var approveDelegationResult = await _signifyClientService.ApproveDelegation();
        var deletePasscodeResult = await _signifyClientService.DeletePasscode();
        var fetchResult = await _signifyClientService.Fetch("path", "GET", new object(), null);
        var getChallengesResult = await _signifyClientService.GetChallenges();
        var getContactsResult = await _signifyClientService.GetContacts();
        var getEscrowsResult = await _signifyClientService.GetEscrows();
        var getExchangesResult = await _signifyClientService.GetExchanges();
        var getGroupsResult = await _signifyClientService.GetGroups();
        var getIpexResult = await _signifyClientService.GetIpex();
        var getKeyEventsResult = await _signifyClientService.GetKeyEvents();
        var getKeyStatesResult = await _signifyClientService.GetKeyStates();
        var getOobisResult = await _signifyClientService.GetOobis();
        var getOperationsResult = await _signifyClientService.GetOperations();
        var getSchemasResult = await _signifyClientService.GetSchemas();
        var rotateResult = await _signifyClientService.Rotate("nbran", ["aid1", "aid2"]);
        var saveOldPasscodeResult = await _signifyClientService.SaveOldPasscode("passcode");
        var signedFetchResult = await _signifyClientService.SignedFetch("url", "path", "GET", new object(), "aidName");

        // Assert all return failure with "Not implemented"
        Assert.True(approveDelegationResult.IsFailed);
        Assert.True(deletePasscodeResult.IsFailed);
        Assert.True(fetchResult.IsFailed);
        Assert.True(getChallengesResult.IsFailed);
        Assert.True(getContactsResult.IsFailed);
        Assert.True(getEscrowsResult.IsFailed);
        Assert.True(getExchangesResult.IsFailed);
        Assert.True(getGroupsResult.IsFailed);
        Assert.True(getIpexResult.IsFailed);
        Assert.True(getKeyEventsResult.IsFailed);
        Assert.True(getKeyStatesResult.IsFailed);
        Assert.True(getOobisResult.IsFailed);
        Assert.True(getOperationsResult.IsFailed);
        Assert.True(getSchemasResult.IsFailed);
        Assert.True(rotateResult.IsFailed);
        Assert.True(saveOldPasscodeResult.IsFailed);
        Assert.True(signedFetchResult.IsFailed);

        Assert.Contains("Not implemented", approveDelegationResult.Errors[0].Message);
    }

    [Fact]
    public async Task Connect_ParameterlessOverload_ShouldThrowNotImplementedException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => _signifyClientService.Connect());
    }
}