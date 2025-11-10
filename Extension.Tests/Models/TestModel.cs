namespace Extension.Tests.Models;

using Extension.Helper;

/// <summary>
/// Test model for storage service tests.
/// Contains properties of various types to test serialization, storage, and notification behavior.
/// </summary>
public sealed record TestModel {
    public required bool BoolProperty { get; init; }
    public required int IntProperty { get; init; }
    public required float FloatProperty { get; init; }
    public required string StringProperty { get; init; }
    public string? NullableStringProperty { get; init; }
    public required RecursiveDictionary RecursiveDictionaryProperty { get; init; }
}
