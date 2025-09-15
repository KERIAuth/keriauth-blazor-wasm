using Xunit;

namespace Extension.Tests.Attributes;

/// <summary>
/// Custom xUnit Fact attribute that skips tests when not running in a browser environment.
/// Used for tests that require Blazor WebAssembly runtime and JavaScript interop.
/// </summary>
public class BrowserOnlyFactAttribute : FactAttribute
{
    public BrowserOnlyFactAttribute()
    {
        if (!OperatingSystem.IsBrowser())
        {
            Skip = "Test requires browser environment with Blazor WebAssembly runtime";
        }
    }
}

/// <summary>
/// Custom xUnit Theory attribute that skips tests when not running in a browser environment.
/// Used for parameterized tests that require Blazor WebAssembly runtime and JavaScript interop.
/// </summary>
public class BrowserOnlyTheoryAttribute : TheoryAttribute
{
    public BrowserOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsBrowser())
        {
            Skip = "Test requires browser environment with Blazor WebAssembly runtime";
        }
    }
}