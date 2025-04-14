using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Tests.Helper
{
    public class UpperCaseTests
    {
        [Fact]
        public void ConvertName_ShouldConvertToUppercase()
        {
            // Arrange
            var upperCase = new UpperCase();
            string input = "testString";
            
            // Act
            string result = upperCase.ConvertName(input);
            
            // Assert
            Assert.Equal("TESTSTRING", result);
        }
        
        [Fact]
        public void ConvertName_ShouldHandleAlreadyUppercaseStrings()
        {
            // Arrange
            var upperCase = new UpperCase();
            string input = "ALREADYUPPERCASE";
            
            // Act
            string result = upperCase.ConvertName(input);
            
            // Assert
            Assert.Equal("ALREADYUPPERCASE", result);
        }
        
        [Fact]
        public void ConvertName_ShouldHandleMixedCaseStrings()
        {
            // Arrange
            var upperCase = new UpperCase();
            string input = "MixedCASEstring123";
            
            // Act
            string result = upperCase.ConvertName(input);
            
            // Assert
            Assert.Equal("MIXEDCASESTRING123", result);
        }
        
        [Fact]
        public void ConvertName_ShouldThrowExceptionForNullInput()
        {
            // Arrange
            var upperCase = new UpperCase();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => upperCase.ConvertName(null!));
        }
        
        [Fact]
        public void ConvertName_ShouldHandleEmptyString()
        {
            // Arrange
            var upperCase = new UpperCase();
            string input = "";
            
            // Act
            string result = upperCase.ConvertName(input);
            
            // Assert
            Assert.Equal("", result);
        }
    }
}