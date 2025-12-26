using Extension.Helper;

namespace Extension.Tests.Helper {
    public class Bip39EnglishWordListTests {
        [Fact]
        public void Words_ShouldContainExpectedWordCount() {
            // Arrange & Act
            var actualCount = Bip39EnglishWordList.Words.Length;

            // Assert - BIP39 defines exactly 2048 words
            Assert.Equal(2048, actualCount);
        }

        [Fact]
        public void Words_ShouldContainSpecificWords() {
            // Arrange & Act
            var words = Bip39EnglishWordList.Words;

            // Assert - First and last words per BIP39 specification
            Assert.Equal("abandon", words[0]);
            Assert.Equal("zoo", words[2047]);

            // Assert - Other known words
            Assert.Contains("ability", words);
            Assert.Contains("zero", words);
            Assert.Contains("zone", words);
        }

        [Fact]
        public void Words_ShouldNotContainEmptyStrings() {
            // Arrange & Act
            var words = Bip39EnglishWordList.Words;

            // Assert
            Assert.DoesNotContain(string.Empty, words);
            Assert.DoesNotContain(null, words);
        }

        [Fact]
        public void Words_ShouldContainOnlyLowercaseWords() {
            // Arrange & Act
            var words = Bip39EnglishWordList.Words;

            // Assert
            foreach (var word in words) {
                Assert.Equal(word.ToLowerInvariant(), word);
            }
        }

        [Fact]
        public void ValidateWords_ShouldReturnTrueForValidWords() {
            // Arrange
            var validWords = new[] { "abandon", "ability", "able", "zoo", "zone" };

            // Act
            var result = Bip39EnglishWordList.ValidateWords(validWords);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateWords_ShouldReturnFalseForInvalidWords() {
            // Arrange
            var invalidWords = new[] { "abandon", "notavalidword", "zoo" };

            // Act
            var result = Bip39EnglishWordList.ValidateWords(invalidWords);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ValidateWords_ShouldBeCaseInsensitive() {
            // Arrange
            var mixedCaseWords = new[] { "ABANDON", "Ability", "ABLE" };

            // Act
            var result = Bip39EnglishWordList.ValidateWords(mixedCaseWords);

            // Assert
            Assert.True(result);
        }
    }
}
