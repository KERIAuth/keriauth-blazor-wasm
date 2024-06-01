namespace KeriAuth.BrowserExtension.Helper;

using Jdenticon;
using System.Diagnostics;


public class Identicon
{
    public static string MakeIdenticon(string value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        // https://jdenticon.com/icon-designer.html?config=000000ff0141640026641e5a

        // Create a vibrant background color hue, with optimal saturation and billiance.
        // Derive a deterministic hue value between [0, 1] from a hash of the provide string
        int hashInt = Math.Abs(BitConverter.ToInt32(HashGenerator.ComputeHash(value, "SHA1"), 0));
        float backColorHue = hashInt % 100 / 100f;

        Jdenticon.Identicon.DefaultStyle = new IdenticonStyle
        {
            BackColor = Jdenticon.Rendering.Color.FromHsl(backColorHue, 1f, 0.5f),
            ColorLightness = Range.Create(0f, 1f),
            GrayscaleLightness = Range.Create(0f, 1f),
            ColorSaturation = 1.00f,
            GrayscaleSaturation = 0.00f
        };
        var icon = Jdenticon.Identicon.FromValue(value, size: 100);
        Debug.Assert(icon is not null);
        return icon.ToSvg(false);
    }
}