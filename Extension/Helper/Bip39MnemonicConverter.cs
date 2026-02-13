using System.Security.Cryptography;
using System.Text;

namespace Extension.Helper;

/// <summary>
/// Converts between 21-character Signify passcodes and 18-word BIP39 mnemonic phrases.
///
/// Algorithm (matching Veridian wallet implementation):
/// - Passcode to Mnemonic:
///   1. UTF-8 encode 21-char passcode (21 bytes)
///   2. Pad with 3 zero bytes to get 24 bytes (192 bits entropy)
///   3. Compute SHA-256 hash of the 24 bytes
///   4. Append first 6 bits of hash as checksum (192 / 32 = 6 bits)
///   5. Total: 192 + 6 = 198 bits
///   6. Split into 18 groups of 11 bits (198 / 11 = 18)
///   7. Each 11-bit value (0-2047) maps to a BIP39 word
///
/// - Mnemonic to Passcode:
///   1. Convert 18 words to 18 eleven-bit indices
///   2. Concatenate to get 198 bits
///   3. Extract first 192 bits as entropy (last 6 bits are checksum)
///   4. Convert 24 bytes to UTF-8, trim trailing zeros to get 21-char passcode
///
/// Reference: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
/// </summary>
public static class Bip39MnemonicConverter
{
    private const int PasscodeLength = 21;
    private const int PaddingBytes = 3;
    private const int EntropyBytes = PasscodeLength + PaddingBytes; // 24 bytes = 192 bits
    private const int ChecksumBits = 6; // 192 / 32 = 6
    private const int TotalBits = 198; // 192 + 6
    private const int BitsPerWord = 11;
    private const int WordCount = 18; // 198 / 11 = 18

    /// <summary>
    /// Converts a 21-character Signify passcode to an 18-word BIP39 mnemonic phrase.
    /// </summary>
    /// <param name="passcode">The 21-character passcode</param>
    /// <returns>Array of 18 BIP39 words</returns>
    /// <exception cref="ArgumentException">If passcode is not exactly 21 characters</exception>
    public static string[] ConvertPasscodeToMnemonic(string passcode)
    {
        if (string.IsNullOrEmpty(passcode) || passcode.Length != PasscodeLength)
        {
            throw new ArgumentException($"Passcode must be exactly {PasscodeLength} characters", nameof(passcode));
        }

        // Step 1: UTF-8 encode passcode and pad with zeros
        byte[] entropy = new byte[EntropyBytes];
        int bytesWritten = Encoding.UTF8.GetBytes(passcode, entropy.AsSpan());

        // Verify UTF-8 encoding produced expected bytes (ASCII chars = 1 byte each)
        if (bytesWritten != PasscodeLength)
        {
            throw new ArgumentException("Passcode must contain only single-byte UTF-8 characters", nameof(passcode));
        }
        // Remaining 3 bytes are already zero from array initialization

        // Step 2: Compute SHA-256 hash for checksum
        byte[] hash = SHA256.HashData(entropy);

        // Step 3: Extract first 6 bits of hash as checksum
        int checksumValue = (hash[0] >> 2) & 0x3F; // Top 6 bits of first byte

        // Step 4: Convert entropy + checksum to word indices
        // We need to process 192 bits of entropy + 6 bits of checksum = 198 bits
        // Split into 18 groups of 11 bits each
        string[] words = new string[WordCount];

        // Convert entropy bytes to a bit stream and extract 11-bit indices
        for (int wordIndex = 0; wordIndex < WordCount; wordIndex++)
        {
            int bitPosition = wordIndex * BitsPerWord;
            int index = Extract11Bits(entropy, checksumValue, bitPosition);
            words[wordIndex] = Bip39EnglishWordList.Words[index];
        }

        return words;
    }

    /// <summary>
    /// Checks if all words in the array are valid BIP39 words.
    /// </summary>
    /// <param name="words">Array of words to check</param>
    /// <returns>True if all words are valid BIP39 words, false otherwise</returns>
    public static bool AreAllValidBip39Words(string[] words)
    {
        if (words == null || words.Length == 0)
        {
            return false;
        }

        var wordSet = new HashSet<string>(Bip39EnglishWordList.Words, StringComparer.OrdinalIgnoreCase);

        return words.All(word => !string.IsNullOrWhiteSpace(word) && wordSet.Contains(word.Trim()));
    }

    /// <summary>
    /// Converts an 18-word BIP39 mnemonic phrase back to a 21-character Signify passcode.
    /// </summary>
    /// <param name="words">Array of 18 BIP39 words</param>
    /// <returns>The 21-character passcode</returns>
    /// <exception cref="ArgumentException">If words array is invalid or contains non-BIP39 words</exception>
    public static string ConvertMnemonicToPasscode(string[] words)
    {
        if (words == null || words.Length != WordCount)
        {
            throw new ArgumentException($"Mnemonic must be exactly {WordCount} words", nameof(words));
        }

        // Build word index lookup for O(1) access
        var wordToIndex = Bip39EnglishWordList.Words
            .Select((word, index) => (word, index))
            .ToDictionary(x => x.word, x => x.index, StringComparer.OrdinalIgnoreCase);

        // Convert words to 11-bit indices
        int[] indices = new int[WordCount];
        for (int i = 0; i < WordCount; i++)
        {
            string word = words[i].ToLowerInvariant().Trim();
            if (!wordToIndex.TryGetValue(word, out int index))
            {
                throw new ArgumentException($"Invalid BIP39 word at position {i + 1}: '{words[i]}'", nameof(words));
            }
            indices[i] = index;
        }

        // Reconstruct the 198 bits from the 18 eleven-bit indices
        // First 192 bits = entropy, last 6 bits = checksum
        byte[] entropy = new byte[EntropyBytes];

        // Pack the 11-bit indices back into bytes
        int bitPosition = 0;
        foreach (int index in indices)
        {
            Write11Bits(entropy, index, bitPosition);
            bitPosition += BitsPerWord;
        }

        // The last 6 bits (checksum) end up partially in the last byte
        // We only need the first 24 bytes (192 bits) as entropy
        // The checksum bits that overflow into position 192-197 can be ignored

        // Verify checksum
        byte[] hash = SHA256.HashData(entropy);
        int expectedChecksum = (hash[0] >> 2) & 0x3F;
        int actualChecksum = ExtractChecksumFromIndices(indices);

        if (expectedChecksum != actualChecksum)
        {
            throw new ArgumentException("Invalid mnemonic: checksum verification failed", nameof(words));
        }

        // Convert entropy bytes to passcode string (trim trailing zero padding)
        string passcode = Encoding.UTF8.GetString(entropy, 0, PasscodeLength);

        return passcode;
    }

    /// <summary>
    /// Extracts 11 bits starting at the given bit position from entropy + checksum.
    /// </summary>
    private static int Extract11Bits(byte[] entropy, int checksumValue, int bitPosition)
    {
        int result = 0;

        for (int i = 0; i < BitsPerWord; i++)
        {
            int currentBitPos = bitPosition + i;
            int bit;

            if (currentBitPos < 192)
            {
                // Bit is in entropy
                int byteIndex = currentBitPos / 8;
                int bitIndex = 7 - (currentBitPos % 8); // MSB first
                bit = (entropy[byteIndex] >> bitIndex) & 1;
            }
            else
            {
                // Bit is in checksum (bits 192-197)
                int checksumBitIndex = 5 - (currentBitPos - 192); // MSB first within checksum
                bit = (checksumValue >> checksumBitIndex) & 1;
            }

            result = (result << 1) | bit;
        }

        return result;
    }

    /// <summary>
    /// Writes 11 bits at the given bit position into the entropy array.
    /// Only writes to the entropy portion (first 192 bits).
    /// </summary>
    private static void Write11Bits(byte[] entropy, int value, int bitPosition)
    {
        for (int i = 0; i < BitsPerWord; i++)
        {
            int currentBitPos = bitPosition + i;

            if (currentBitPos >= 192)
            {
                // Skip checksum bits - they don't go into entropy
                continue;
            }

            int byteIndex = currentBitPos / 8;
            int bitIndex = 7 - (currentBitPos % 8); // MSB first
            int bit = (value >> (BitsPerWord - 1 - i)) & 1;

            if (bit == 1)
            {
                entropy[byteIndex] |= (byte)(1 << bitIndex);
            }
        }
    }

    /// <summary>
    /// Extracts the 6-bit checksum from the last bits of the word indices.
    /// The checksum is bits 192-197, spread across the last word's lower bits.
    /// </summary>
    private static int ExtractChecksumFromIndices(int[] indices)
    {
        // The 198 bits are: 192 bits entropy + 6 bits checksum
        // Word 17 (last word, index 17) starts at bit 187 (17 * 11 = 187)
        // It covers bits 187-197, so bits 192-197 are the last 6 bits of the last word
        int lastWordIndex = indices[WordCount - 1];

        // The last word covers bits 187-197
        // Bits 187-191 are entropy (5 bits)
        // Bits 192-197 are checksum (6 bits)
        // So checksum is the last 6 bits of the last word's 11-bit value
        return lastWordIndex & 0x3F;
    }
}
