namespace Extension.Tests.Models;

using Extension.Models.Storage;

/// <summary>
/// Test-only storage model that replaces PasscodeModel for unit tests.
/// Used by storage service and observer tests that need a model with string and DateTime properties.
/// Not used in production code.
/// </summary>
public record TestPasscodeModel : IStorageModel {
    public required string Passcode { get; init; }
    public DateTime SessionExpirationUtc { get; init; } = DateTime.MinValue;
}
