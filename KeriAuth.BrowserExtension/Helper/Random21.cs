using System.Security.Cryptography;
using System.Text;

namespace KeriAuth.BrowserExtension.Helper
{
    // TODO P1 this is temporary and must be replaced with a more secure implementation
    public class RandomStringGenerator
    {
        private static readonly char[] _characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        public static string GenerateRandomString(int length)
        {
            if (length <= 0)
            {
                throw new ArgumentException("Length must be a positive integer", nameof(length));
            }

            var stringBuilder = new StringBuilder(length);
            using (var rng = RandomNumberGenerator.Create())
            {
                var data = new byte[length];
                rng.GetBytes(data);

                foreach (var byteValue in data)
                {
                    var index = byteValue % _characters.Length;
                    stringBuilder.Append(_characters[index]);
                }
            }

            return stringBuilder.ToString();
        }
    }
}
