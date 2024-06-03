using MudBlazor;
using System.Net.NetworkInformation;
using System.Text;
using System.Security.Cryptography;

namespace KeriAuth.BrowserExtension.Helper
{
    public class GetNumberFromHash
    {
        public static int HashInt(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                int hashInt = BitConverter.ToInt32(hashBytes, 0);
                hashInt = Math.Abs(hashInt);
                return hashInt;
            }
        }
    }
}