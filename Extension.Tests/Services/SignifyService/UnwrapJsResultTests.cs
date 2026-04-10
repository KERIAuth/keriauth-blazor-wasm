using Extension.Models;
using Extension.Services.JsBindings;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Extension.Tests.Services.SignifyService;

/// <summary>
/// Tests for the UnwrapJsResult logic in SignifyClientService.
/// Uses GetState as the simplest path through UnwrapJsResult, since all
/// ~47 withClientOperation-based methods share this same unwrapping code.
/// </summary>
public class UnwrapJsResultTests {
    private readonly Mock<ISignifyClientBinding> _mockBinding = new();
    private readonly SignifyClientService _service;

    // Valid state JSON matching what signify-ts returns
    private const string ValidStateJson = """{"agent":{"i":"EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E"},"controller":{"state":{"i":"EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u","s":"0","k":["DKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"]}},"ridx":0,"pidx":0}""";

    public UnwrapJsResultTests() {
        var mockLogger = new Mock<ILogger<SignifyClientService>>();
        _service = new SignifyClientService(mockLogger.Object, _mockBinding.Object);
    }

    // Helper to build a success Result envelope
    private static string OkEnvelope(string valueJson) => $@"{{""ok"":true,""value"":{valueJson}}}";

    // Helper to build an error Result envelope
    private static string ErrEnvelope(string code, string message) =>
        $@"{{""ok"":false,""code"":""{code}"",""message"":""{EscapeJson(message)}""}}";

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [Fact]
    public async Task GetState_SuccessEnvelope_ReturnsOkWithDeserializedState() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkEnvelope(ValidStateJson));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E", result.Value.Agent?.I);
        Assert.Equal("EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", result.Value.Controller?.State?.I);
    }

    [Fact]
    public async Task GetState_NotConnectedError_ReturnsNotConnectedError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("not_connected", "signifyClient: validateClient - Client not connected"));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<NotConnectedError>(error);
    }

    [Fact]
    public async Task GetState_NetworkError_ReturnsConnectionError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("network_error", "Failed to fetch"));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<ConnectionError>(error);
    }

    [Fact]
    public async Task GetState_OperationTimeoutError_ReturnsOperationTimeoutError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("operation_timeout", "The operation timed out."));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<OperationTimeoutError>(error);
    }

    [Fact]
    public async Task GetState_ValidationError_ReturnsValidationError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("validation_error", "Invalid argument: name is required"));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<ValidationError>(error);
    }

    [Fact]
    public async Task GetState_UnknownErrorCode_ReturnsJavaScriptInteropError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("unknown", "Something unexpected happened"));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
    }

    [Fact]
    public async Task GetState_KeriaErrorCode_ReturnsJavaScriptInteropError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("keria_error", "404 - Not Found"));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
    }

    [Fact]
    public async Task GetState_MalformedJson_ReturnsJavaScriptInteropError() {
        // Arrange - not a valid Result envelope
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("this is not json at all");

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
        Assert.Contains("Invalid Result envelope", error.Message);
    }

    [Fact]
    public async Task GetState_NullResponse_ReturnsJavaScriptInteropError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null!);

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
        Assert.Contains("Null or empty", error.Message);
    }

    [Fact]
    public async Task GetState_EmptyStringResponse_ReturnsJavaScriptInteropError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
        Assert.Contains("Null or empty", error.Message);
    }

    [Fact]
    public async Task GetState_SuccessWithNullValue_ReturnsJavaScriptInteropError() {
        // Arrange - ok:true but value is null
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"{""ok"":true,""value"":null}");

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
        Assert.Contains("null/empty value", error.Message);
    }

    [Fact]
    public async Task GetState_OperationErrorCode_ReturnsJavaScriptInteropError() {
        // Arrange
        _mockBinding
            .Setup(b => b.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrEnvelope("operation_error", "Operation xyz failed: timeout"));

        // Act
        var result = await _service.GetState();

        // Assert
        Assert.True(result.IsFailed);
        var error = Assert.Single(result.Errors);
        Assert.IsType<JavaScriptInteropError>(error);
    }

    // Reachability tracking tests moved to SignifyRequestBrokerTests.
    // The broker now owns consecutive-failure-based reachability, not SignifyClientService.
}
