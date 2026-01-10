using System.Security.Cryptography;
using Extension.Helper;

namespace Extension.Tests.Helper;

public class Bip39MnemonicConverterTests
{
    /// <summary>
    /// Test A: Random 21-char passcode → 18 words → passcode (round-trip match)
    /// </summary>
    [Fact]
    public void PasscodeToMnemonicToPasscode_ShouldRoundTrip()
    {
        // Arrange: Generate a random 21-character passcode
        string originalPasscode = RandomStringGenerator.GenerateRandomString(21);

        // Act: Convert to mnemonic and back
        string[] mnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(originalPasscode);
        string recoveredPasscode = Bip39MnemonicConverter.ConvertMnemonicToPasscode(mnemonic);

        // Assert
        Assert.Equal(18, mnemonic.Length);
        Assert.All(mnemonic, word => Assert.True(Bip39EnglishWordList.ValidateWords(new[] { word })));
        Assert.Equal(originalPasscode, recoveredPasscode);
    }

    /// <summary>
    /// Test B: Random 18 BIP39 words → passcode → 18 words (round-trip match)
    /// </summary>
    [Fact]
    public void MnemonicToPasscodeToMnemonic_ShouldRoundTrip()
    {
        // Arrange: Generate a valid 18-word mnemonic by creating a random passcode first
        // (We need to start with a valid mnemonic that has correct checksum)
        string tempPasscode = RandomStringGenerator.GenerateRandomString(21);
        string[] originalMnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(tempPasscode);

        // Act: Convert to passcode and back to mnemonic
        string passcode = Bip39MnemonicConverter.ConvertMnemonicToPasscode(originalMnemonic);
        string[] recoveredMnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(passcode);

        // Assert
        Assert.Equal(21, passcode.Length);
        Assert.Equal(18, recoveredMnemonic.Length);
        Assert.Equal(originalMnemonic, recoveredMnemonic);
    }

    /// <summary>
    /// Multiple random round-trips to increase confidence
    /// </summary>
    [Theory]
    [InlineData(10)]
    public void PasscodeToMnemonicToPasscode_MultipleRandomTrials_ShouldAllRoundTrip(int trials)
    {
        for (int i = 0; i < trials; i++)
        {
            // Arrange
            string originalPasscode = RandomStringGenerator.GenerateRandomString(21);

            // Act
            string[] mnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(originalPasscode);
            string recoveredPasscode = Bip39MnemonicConverter.ConvertMnemonicToPasscode(mnemonic);

            // Assert
            Assert.Equal(originalPasscode, recoveredPasscode);
        }
    }

    /// <summary>
    /// Verify mnemonic words are all valid BIP39 words
    /// </summary>
    [Fact]
    public void ConvertPasscodeToMnemonic_ShouldProduceValidBip39Words()
    {
        // Arrange
        string passcode = RandomStringGenerator.GenerateRandomString(21);

        // Act
        string[] mnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(passcode);

        // Assert
        Assert.Equal(18, mnemonic.Length);
        Assert.True(Bip39EnglishWordList.ValidateWords(mnemonic));
    }

    /// <summary>
    /// Invalid passcode length should throw
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("this is way too long for a passcode")]
    [InlineData("exactly20characters0")]  // 20 chars
    [InlineData("exactly22characters012")]  // 23 chars
    public void ConvertPasscodeToMnemonic_InvalidLength_ShouldThrow(string invalidPasscode)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Bip39MnemonicConverter.ConvertPasscodeToMnemonic(invalidPasscode));
    }

    /// <summary>
    /// Invalid mnemonic word count should throw
    /// </summary>
    [Fact]
    public void ConvertMnemonicToPasscode_InvalidWordCount_ShouldThrow()
    {
        // Arrange
        string[] tooFewWords = new[] { "abandon", "ability", "able" };
        string[] tooManyWords = Enumerable.Repeat("abandon", 24).ToArray();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Bip39MnemonicConverter.ConvertMnemonicToPasscode(tooFewWords));
        Assert.Throws<ArgumentException>(() =>
            Bip39MnemonicConverter.ConvertMnemonicToPasscode(tooManyWords));
    }

    /// <summary>
    /// Invalid BIP39 word should throw
    /// </summary>
    [Fact]
    public void ConvertMnemonicToPasscode_InvalidWord_ShouldThrow()
    {
        // Arrange: Create valid mnemonic then corrupt one word
        string passcode = RandomStringGenerator.GenerateRandomString(21);
        string[] mnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(passcode);
        mnemonic[5] = "notavalidword";

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Bip39MnemonicConverter.ConvertMnemonicToPasscode(mnemonic));
    }

    /// <summary>
    /// Invalid checksum should throw
    /// </summary>
    [Fact]
    public void ConvertMnemonicToPasscode_InvalidChecksum_ShouldThrow()
    {
        // Arrange: Create valid mnemonic then change a word to corrupt checksum
        string passcode = RandomStringGenerator.GenerateRandomString(21);
        string[] mnemonic = Bip39MnemonicConverter.ConvertPasscodeToMnemonic(passcode);

        // Change a word in the middle (not the last word which contains checksum)
        // This should corrupt the entropy but keep valid BIP39 words
        int originalIndex = Array.IndexOf(Bip39EnglishWordList.Words, mnemonic[3]);
        int newIndex = (originalIndex + 1) % 2048;
        mnemonic[3] = Bip39EnglishWordList.Words[newIndex];

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Bip39MnemonicConverter.ConvertMnemonicToPasscode(mnemonic));
    }
}
