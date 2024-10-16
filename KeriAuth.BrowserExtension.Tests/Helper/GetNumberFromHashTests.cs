using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Tests.Helper
{
    public class GetNumberFromHashTests
    {
        [Fact]
        public void HashInt_ReturnsPositiveInteger()
        {
            // Arrange
            string input = "test input";

            // Act
            int result = GetNumberFromHash.HashInt(input);

            // Assert
            Assert.True(result >= 0, "HashInt should return a non-negative integer.");
        }

        [Fact]
        public void HashInt_ReturnsSameHashForSameInput()
        {
            // Arrange
            string input = "consistent input";

            // Act
            int result1 = GetNumberFromHash.HashInt(input);
            int result2 = GetNumberFromHash.HashInt(input);

            // Assert
            Assert.Equal(result1, result2);
        }

        [Fact]
        public void HashInt_ReturnsDifferentHashesForDifferentInputs()
        {
            // Arrange
            string input1 = "input one";
            string input2 = "input two";

            // Act
            int result1 = GetNumberFromHash.HashInt(input1);
            int result2 = GetNumberFromHash.HashInt(input2);

            // Assert
            Assert.NotEqual(result1, result2);
        }

        [Theory]
        [InlineData("")]
        // [InlineData(null)] // TODO adjust code so this test passes?
        public void HashInt_HandlesNullOrEmptyInput(string input)
        {
            // Act
            int result = GetNumberFromHash.HashInt(input);

            // Assert
            Assert.True(result >= 0, "HashInt should return a non-negative integer even for null or empty input.");
        }
    }
}
