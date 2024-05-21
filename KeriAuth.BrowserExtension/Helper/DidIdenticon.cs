namespace KeriAuth.BrowserExtension.Helper;

using Jdenticon;
using System.Text;

public class DidIdenticon
{
    public static string MakeIdenticon(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return "";
        }
        // Custom identicon style

        // https://jdenticon.com/icon-designer.html?config=000000ff0141640026641e5a
        // Create a vibrant background color hue, with optimal saturation and billiance.

        // Derive a deterministic hue value between [0, 1] from a hash of the provide string
        // float hue = Math.Abs(BitConverter.ToInt32(NBitcoin.Crypto.Hashes.SHA512(Encoding.ASCII.GetBytes(value)))) % 100 / 100f;
        float hue = 100f;

        Identicon.DefaultStyle = new IdenticonStyle
        {
            BackColor = Jdenticon.Rendering.Color.FromHsl(hue, 1f, 0.5f),
            ColorLightness = Range.Create(0f, 1f),
            GrayscaleLightness = Range.Create(0f, 1f),
            ColorSaturation = 1.00f,
            GrayscaleSaturation = 0.00f
        };
        var icon = Identicon.FromValue(value, size: 100);
        return icon.ToSvg(false);
    }
}