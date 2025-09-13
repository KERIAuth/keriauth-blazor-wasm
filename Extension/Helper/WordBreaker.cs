namespace Extension.Helper;

public static class WordBreaker {
    /// <summary>
    /// Helper for avoid Snackbar Messages or other user-facing Error messages to grow out of proportion.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="maxWordLength">Defines the longest word in the text. Any longer at it will be shortend</param>
    /// <param name="maxTextLength">Defines the max-length of the overall text</param>
    /// <returns></returns>
    public static string Break(string input, int maxWordLength = 25, int maxTextLength = 300) {
        var splitted = input.Split(' ');
        for (var index = 0; index < splitted.Length; index++) {
            var s = splitted[index];
            if (s.Length > maxWordLength) {
                splitted[index] = $"{s[..maxWordLength]}...";
            }
        }
        var joinedString = string.Join(' ', splitted);
        if (joinedString.Length > maxTextLength) {
            return $"{joinedString[..(maxTextLength - 1)]}...";
        }

        return joinedString;
    }
}
