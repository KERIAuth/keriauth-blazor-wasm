using System.Security.Cryptography;
using System.Text;

namespace Extension.Helper
{
    public class GetNumberFromHash
    {
        public static int HashInt(string input)
        {
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            int hashInt = BitConverter.ToInt32(hashBytes, 0);
            hashInt = Math.Abs(hashInt);
            return hashInt;
        }
    }
}