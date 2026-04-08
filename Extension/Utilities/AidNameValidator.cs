namespace Extension.Utilities;

using System.Text.RegularExpressions;

public static partial class AidNameValidator {
    private const string WarningMessage =
        "The name must be 32 characters or less, with only lowercase letters, numbers, _, -, and without any whitespace.";

    // TODO P3 The following regex may be too restrictive, but would need to confirm with Veridian, signify-ts, KERIA, and KERIPY on actual restrictions.
    // Veridian's plan may be to allow friendly names to be stored in a separate hab.
    [GeneratedRegex(@"^[a-z0-9_-]{1,32}$")]
    private static partial Regex AidNamePattern();

    private const string OptionalWarningMessage =
        "If provided, the prefix must be 32 characters or less, with only lowercase letters, numbers, _, -, and without any whitespace.";

    [GeneratedRegex(@"^[a-z0-9_-]{0,32}$")]
    private static partial Regex OptionalAidNamePattern();

    /// <summary>
    /// Returns null if the name is valid, or a warning message string if invalid.
    /// </summary>
    public static string? Validate(string? name) {
        if (string.IsNullOrEmpty(name) || !AidNamePattern().IsMatch(name))
            return WarningMessage;
        return null;
    }

    /// <summary>
    /// Like Validate, but allows null or empty (for optional fields).
    /// </summary>
    public static string? ValidateOptional(string? name) {
        if (name is null || name.Length == 0)
            return null;
        if (!OptionalAidNamePattern().IsMatch(name))
            return OptionalWarningMessage;
        return null;
    }
}
