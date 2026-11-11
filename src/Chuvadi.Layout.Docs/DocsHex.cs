using System.Globalization;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.Docs
{
    /// <summary>
    /// Reads a six-digit RRGGBB colour from the document. Theme colours and fonts come from Chuvadi.Docs since 2.1.0
    /// (<c>Document.Theme</c>, <c>Document.GetThemeColorHex</c>; D-317 T1), which replaced Layout's own theme parser.
    /// </summary>
    internal static class DocsHex
    {
        internal static bool TryHex(string? hex, out LayoutColor color)
        {
            color = LayoutColor.Black;
            if (hex is null || hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            {
                return false;
            }
            color = new LayoutColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
    }
}
