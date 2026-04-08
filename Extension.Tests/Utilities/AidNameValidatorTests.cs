using Extension.Utilities;

namespace Extension.Tests.Utilities;

public class AidNameValidatorTests {
    [Theory]
    [InlineData("myaid")]
    [InlineData("my-aid")]
    [InlineData("my_aid")]
    [InlineData("a")]
    [InlineData("123")]
    [InlineData("a-b_c-1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // 32 chars
    public void Validate_ValidNames_ReturnsNull(string name) {
        Assert.Null(AidNameValidator.Validate(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MyAid")]           // uppercase
    [InlineData("my aid")]          // space
    [InlineData("my.aid")]          // dot
    [InlineData("my@aid")]          // special char
    [InlineData(" leading")]        // leading whitespace
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33 chars
    public void Validate_InvalidNames_ReturnsWarning(string? name) {
        var result = AidNameValidator.Validate(name);
        Assert.NotNull(result);
        Assert.Contains("32 characters or less", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("myprefix")]
    [InlineData("a-b_c")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // 32 chars
    public void ValidateOptional_ValidValues_ReturnsNull(string? name) {
        Assert.Null(AidNameValidator.ValidateOptional(name));
    }

    [Theory]
    [InlineData("MyPrefix")]        // uppercase
    [InlineData("my prefix")]       // space
    [InlineData("my.prefix")]       // dot
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33 chars
    public void ValidateOptional_InvalidValues_ReturnsWarning(string name) {
        var result = AidNameValidator.ValidateOptional(name);
        Assert.NotNull(result);
        Assert.Contains("32 characters or less", result);
    }
}
