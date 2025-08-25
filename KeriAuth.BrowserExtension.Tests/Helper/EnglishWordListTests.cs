using KeriAuth.BrowserExtension.Helper;

namespace KeriAuth.BrowserExtension.Tests.Helper
{
    public class EnglishWordListTests
    {
        [Fact]
        public void Words_ShouldContainExpectedWordCount()
        {
            // Arrange & Act
            var actualCount = EnglishWordList.Words.Length;

            // Assert
            Assert.Equal(2048, actualCount);
        }

        [Fact]
        public void Words_ShouldContainSpecificWords()
        {
            // Arrange & Act
            var words = EnglishWordList.Words;

            // Assert
            Assert.Contains("abandon", words);
            Assert.Contains("ability", words);
            Assert.Contains("zero", words);
            Assert.Contains("zone", words);
            Assert.Contains("zoo", words);
        }

        [Fact]
        public void Words_ShouldNotContainEmptyStrings()
        {
            // Arrange & Act
            var words = EnglishWordList.Words;

            // Assert
            Assert.DoesNotContain(string.Empty, words);
            Assert.DoesNotContain(null, words);
        }

        [Fact]
        public void Words_ShouldContainOnlyLowercaseWords()
        {
            // Arrange & Act
            var words = EnglishWordList.Words;

            // Assert
            foreach (var word in words)
            {
                Assert.Equal(word.ToLowerInvariant(), word);
            }
        }
    }
}
