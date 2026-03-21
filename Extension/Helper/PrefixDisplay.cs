namespace Extension.Helper;

using Microsoft.AspNetCore.Components;

public static class PrefixDisplay
{
    /// <summary>
    /// Returns a truncated string with head and tail portions separated by an ellipsis.
    /// When tail is 0, only the head portion is shown followed by an ellipsis.
    /// </summary>
    public static string ElideText(string? value, int head = 4, int tail = 4)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var minLength = tail > 0 ? head + tail + 1 : head + 1;
        if (value.Length <= minLength)
            return value;

        return tail > 0
            ? $"{value[..head]}\u2026{value[^tail..]}"
            : $"{value[..head]}\u2026";
    }

    /// <summary>
    /// Returns a MarkupString wrapping the elided text in a span with class bt-prefix.
    /// </summary>
    public static MarkupString Elide(string? value, int head = 4, int tail = 4) =>
        new($"<span class=\"bt-prefix\">{ElideText(value, head, tail)}</span>");
}
