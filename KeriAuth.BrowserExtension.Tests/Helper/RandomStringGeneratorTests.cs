using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Tests.Helper
{
    public class RandomStringGeneratorTests
    {
        [Fact]
        public void GenerateRandomString_ShouldReturnStringOfCorrectLength()
        {
            // Arrange
            int length = 10;
            
            // Act
            string result = RandomStringGenerator.GenerateRandomString(length);
            
            // Assert
            Assert.Equal(length, result.Length);
        }
        
        [Fact]
        public void GenerateRandomString_ShouldReturnDifferentStringsOnConsecutiveCalls()
        {
            // Arrange
            int length = 20;
            
            // Act
            string result1 = RandomStringGenerator.GenerateRandomString(length);
            string result2 = RandomStringGenerator.GenerateRandomString(length);
            
            // Assert
            Assert.NotEqual(result1, result2);
        }
        
        [Fact]
        public void GenerateRandomString_ShouldContainOnlyAllowedCharacters()
        {
            // Arrange
            int length = 100;
            string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            
            // Act
            string result = RandomStringGenerator.GenerateRandomString(length);
            
            // Assert
            foreach (char c in result)
            {
                Assert.Contains(c, allowedChars);
            }
        }
        
        [Fact]
        public void GenerateRandomString_ShouldThrowExceptionForNegativeLength()
        {
            // Arrange
            int length = -5;
            
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => RandomStringGenerator.GenerateRandomString(length));
            Assert.Contains("Length must be a positive integer", exception.Message);
        }
        
        [Fact]
        public void GenerateRandomString_ShouldThrowExceptionForZeroLength()
        {
            // Arrange
            int length = 0;
            
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => RandomStringGenerator.GenerateRandomString(length));
            Assert.Contains("Length must be a positive integer", exception.Message);
        }
    }
}