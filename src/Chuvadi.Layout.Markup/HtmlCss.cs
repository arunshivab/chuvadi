using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.Markup
{
    /// <summary>CSS value parsing for the HTML importer: colours, lengths and keywords. Values are read, never altered.</summary>
    internal static class HtmlCss
    {
        public const double PxToPt = 0.75;

        /// <summary>Splits a value on whitespace outside parentheses and quotes.</summary>
        public static List<string> Tokens(string value)
        {
            List<string> list = new List<string>();
            int depth = 0;
            char quote = '\0';
            int start = -1;
            for (int i = 0; i <= value.Length; i++)
            {
                char c = i < value.Length ? value[i] : ' ';
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    if (start < 0)
                    {
                        start = i;
                    }
                    continue;
                }
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }
                bool space = depth == 0 && (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || i == value.Length);
                if (space)
                {
                    if (start >= 0)
                    {
                        list.Add(value.Substring(start, i - start));
                        start = -1;
                    }
                }
                else if (start < 0)
                {
                    start = i;
                }
            }
            return list;
        }

        // ---- Lengths ----

        /// <summary>
        /// Parses a CSS length into points. <paramref name="em"/> is the font size in points, <paramref name="rem"/> the
        /// root font size, <paramref name="percentBase"/> what 100% means (null: percentages are not allowed).
        /// </summary>
        public static bool TryLength(string value, double em, double rem, double? percentBase, out double pt)
        {
            pt = 0;
            string v = value.Trim().ToLowerInvariant();
            if (v.Length == 0)
            {
                return false;
            }
            int end = 0;
            while (end < v.Length && (char.IsDigit(v[end]) || v[end] == '.' || ((v[end] == '-' || v[end] == '+') && end == 0) || (v[end] == 'e' && end > 0 && end + 1 < v.Length && (char.IsDigit(v[end + 1]) || v[end + 1] == '-'))))
            {
                end++;
            }
            if (end == 0 || !double.TryParse(v.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n))
            {
                return false;
            }
            string unit = v.Substring(end).Trim();
            switch (unit)
            {
                case "":
                    // Unitless: only zero is a length in CSS; HTML attributes are pixels (handled by the caller).
                    if (n != 0)
                    {
                        return false;
                    }
                    pt = 0;
                    return true;
                case "px":
                    pt = n * PxToPt;
                    return true;
                case "pt":
                    pt = n;
                    return true;
                case "pc":
                    pt = n * 12;
                    return true;
                case "in":
                    pt = n * 72;
                    return true;
                case "cm":
                    pt = n * 72 / 2.54;
                    return true;
                case "mm":
                    pt = n * 72 / 25.4;
                    return true;
                case "q":
                    pt = n * 72 / 101.6;
                    return true;
                case "em":
                    pt = n * em;
                    return true;
                case "rem":
                    pt = n * rem;
                    return true;
                case "ex":
                case "ch":
                    pt = n * em / 2;
                    return true;
                case "%":
                    if (percentBase is double b)
                    {
                        pt = n * b / 100;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>An HTML length attribute ("600", "600px", "50%") in points; percentages of <paramref name="percentBase"/>.</summary>
        public static bool TryAttributeLength(string? value, double? percentBase, out double pt, out bool percent)
        {
            pt = 0;
            percent = false;
            if (value is null)
            {
                return false;
            }
            string v = value.Trim();
            int end = 0;
            while (end < v.Length && (char.IsDigit(v[end]) || v[end] == '.'))
            {
                end++;
            }
            if (end == 0 || !double.TryParse(v.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            {
                return false;
            }
            if (end < v.Length && v[end] == '%')
            {
                percent = true;
                if (percentBase is double b)
                {
                    pt = n * b / 100;
                    return true;
                }
                return false;
            }
            pt = n * PxToPt;
            return true;
        }

        /// <summary>The font size for a CSS font-size value, or null.</summary>
        public static double? FontSize(string value, double parent, double rootDefault)
        {
            string v = value.Trim().ToLowerInvariant();
            double medium = rootDefault;
            switch (v)
            {
                case "xx-small": return medium * 9 / 16;
                case "x-small": return medium * 10 / 16;
                case "small": return medium * 13 / 16;
                case "medium": return medium;
                case "large": return medium * 18 / 16;
                case "x-large": return medium * 24 / 16;
                case "xx-large": return medium * 32 / 16;
                case "xxx-large": return medium * 48 / 16;
                case "smaller": return parent / 1.2;
                case "larger": return parent * 1.2;
            }
            if (TryLength(v, parent, rootDefault, parent, out double pt) && pt >= 0)
            {
                return pt;
            }
            return null;
        }

        /// <summary>The size of a legacy <c>&lt;font size&gt;</c> (1 to 7, or +n / -n relative to 3).</summary>
        public static double? LegacyFontSize(string? value, double rootDefault)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string v = value.Trim();
            int sign = 0;
            if (v[0] == '+' || v[0] == '-')
            {
                sign = v[0] == '+' ? 1 : -1;
                v = v.Substring(1);
            }
            int end = 0;
            while (end < v.Length && char.IsDigit(v[end]))
            {
                end++;
            }
            if (end == 0 || !int.TryParse(v.AsSpan(0, Math.Min(end, 6)), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                return null;
            }
            int size = Math.Clamp(sign == 0 ? n : 3 + (sign * n), 1, 7);
            double[] px = { 10, 13, 16, 18, 24, 32, 48 };
            return rootDefault * px[size - 1] / 16;
        }

        // ---- Colours ----

        /// <summary>Parses a CSS colour. "transparent" gives a colour with alpha 0; "currentcolor" gives <paramref name="current"/>.</summary>
        public static bool TryColor(string value, LayoutColor current, out LayoutColor color)
        {
            color = default;
            string v = value.Trim().ToLowerInvariant();
            if (v.Length == 0)
            {
                return false;
            }
            if (v == "currentcolor")
            {
                color = current;
                return true;
            }
            if (v == "transparent")
            {
                color = LayoutColor.Transparent;
                return true;
            }
            if (v[0] == '#')
            {
                return TryHex(v.AsSpan(1), out color);
            }
            if (s_named.TryGetValue(v, out int rgb))
            {
                color = new LayoutColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return true;
            }
            int open = v.IndexOf('(');
            if (open > 0 && v.EndsWith(')'))
            {
                string fn = v.Substring(0, open).Trim();
                string[] args = v.Substring(open + 1, v.Length - open - 2).Replace(",", " ").Replace("/", " / ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                List<string> parts = new List<string>();
                string? alpha = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "/")
                    {
                        alpha = i + 1 < args.Length ? args[i + 1] : null;
                        break;
                    }
                    parts.Add(args[i]);
                }
                if (parts.Count == 4 && alpha is null)
                {
                    alpha = parts[3];
                    parts.RemoveAt(3);
                }
                if (parts.Count != 3)
                {
                    return false;
                }
                byte a = 255;
                if (alpha is not null)
                {
                    if (!TryUnit(alpha, 1, out double av))
                    {
                        return false;
                    }
                    a = (byte)Math.Round(Math.Clamp(av, 0, 1) * 255);
                }
                if (fn is "rgb" or "rgba")
                {
                    if (TryUnit(parts[0], 255, out double r) && TryUnit(parts[1], 255, out double g) && TryUnit(parts[2], 255, out double b))
                    {
                        color = new LayoutColor(Byte(r), Byte(g), Byte(b), a);
                        return true;
                    }
                    return false;
                }
                if (fn is "hsl" or "hsla")
                {
                    if (TryAngle(parts[0], out double h) && TryUnit(parts[1], 1, out double s) && TryUnit(parts[2], 1, out double l))
                    {
                        (double r, double g, double b) = Hsl(h, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1));
                        color = new LayoutColor(Byte(r * 255), Byte(g * 255), Byte(b * 255), a);
                        return true;
                    }
                }
            }
            return false;
        }

        private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 255));

        /// <summary>A number or a percentage (of <paramref name="full"/>).</summary>
        private static bool TryUnit(string s, double full, out double v)
        {
            bool pct = s.EndsWith('%');
            if (double.TryParse(pct ? s.AsSpan(0, s.Length - 1) : s.AsSpan(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v))
            {
                if (pct)
                {
                    v = v * full / 100;
                }
                return true;
            }
            return false;
        }

        private static bool TryAngle(string s, out double degrees)
        {
            string[] units = { "deg", "grad", "rad", "turn" };
            double[] factor = { 1, 0.9, 180 / Math.PI, 360 };
            for (int i = 0; i < units.Length; i++)
            {
                if (s.EndsWith(units[i], StringComparison.Ordinal))
                {
                    bool ok = double.TryParse(s.AsSpan(0, s.Length - units[i].Length), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);
                    degrees *= factor[i];
                    return ok;
                }
            }
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);
        }

        private static (double, double, double) Hsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360;
            double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
            double p = (2 * l) - q;
            return (Hue(p, q, h + (1.0 / 3)), Hue(p, q, h), Hue(p, q, h - (1.0 / 3)));
        }

        private static double Hue(double p, double q, double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        }

        private static bool TryHex(ReadOnlySpan<char> h, out LayoutColor color)
        {
            color = default;
            foreach (char c in h)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }
            int[] d = new int[h.Length];
            for (int i = 0; i < h.Length; i++)
            {
                d[i] = Convert.ToInt32(h[i].ToString(), 16);
            }
            int D(int i) => d[i];
            switch (h.Length)
            {
                case 3:
                    color = new LayoutColor((byte)(D(0) * 17), (byte)(D(1) * 17), (byte)(D(2) * 17));
                    return true;
                case 4:
                    color = new LayoutColor((byte)(D(0) * 17), (byte)(D(1) * 17), (byte)(D(2) * 17), (byte)(D(3) * 17));
                    return true;
                case 6:
                    color = new LayoutColor((byte)((D(0) * 16) + D(1)), (byte)((D(2) * 16) + D(3)), (byte)((D(4) * 16) + D(5)));
                    return true;
                case 8:
                    color = new LayoutColor((byte)((D(0) * 16) + D(1)), (byte)((D(2) * 16) + D(3)), (byte)((D(4) * 16) + D(5)), (byte)((D(6) * 16) + D(7)));
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The WHATWG "rules for parsing a legacy colour value", used for presentational attributes such as
        /// <c>bgcolor</c> and <c>&lt;font color&gt;</c> ("ff0000" without the hash, "red", odd lengths).
        /// </summary>
        public static bool TryLegacyColor(string? value, out LayoutColor color)
        {
            color = default;
            if (value is null)
            {
                return false;
            }
            string v = value.Trim(' ', '\t', '\n', '\r', '\f');
            if (v.Length == 0 || v.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (s_named.TryGetValue(v.ToLowerInvariant(), out int rgb))
            {
                color = new LayoutColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return true;
            }
            if (v.Length == 4 && v[0] == '#' && Uri.IsHexDigit(v[1]) && Uri.IsHexDigit(v[2]) && Uri.IsHexDigit(v[3]))
            {
                return TryHex(v.AsSpan(1), out color);
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(v.Length);
            foreach (Rune r in v.EnumerateRunes())
            {
                sb.Append(r.Value > 0xFFFF ? "00" : r.ToString());
            }
            string s = sb.ToString();
            if (s.Length > 128)
            {
                s = s.Substring(0, 128);
            }
            if (s.Length > 0 && s[0] == '#')
            {
                s = s.Substring(1);
            }
            char[] chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!Uri.IsHexDigit(chars[i]))
                {
                    chars[i] = '0';
                }
            }
            s = new string(chars);
            while (s.Length == 0 || s.Length % 3 != 0)
            {
                s += "0";
            }
            int len = s.Length / 3;
            string c1 = s.Substring(0, len), c2 = s.Substring(len, len), c3 = s.Substring(2 * len, len);
            if (len > 8)
            {
                c1 = c1.Substring(len - 8);
                c2 = c2.Substring(len - 8);
                c3 = c3.Substring(len - 8);
                len = 8;
            }
            while (len > 2 && c1[0] == '0' && c2[0] == '0' && c3[0] == '0')
            {
                c1 = c1.Substring(1);
                c2 = c2.Substring(1);
                c3 = c3.Substring(1);
                len--;
            }
            if (len > 2)
            {
                c1 = c1.Substring(0, 2);
                c2 = c2.Substring(0, 2);
                c3 = c3.Substring(0, 2);
            }
            color = new LayoutColor(Convert.ToByte(c1, 16), Convert.ToByte(c2, 16), Convert.ToByte(c3, 16));
            return true;
        }

        private static readonly Dictionary<string, int> s_named = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["aliceblue"] = 0xF0F8FF, ["antiquewhite"] = 0xFAEBD7, ["aqua"] = 0x00FFFF, ["aquamarine"] = 0x7FFFD4, ["azure"] = 0xF0FFFF,
            ["beige"] = 0xF5F5DC, ["bisque"] = 0xFFE4C4, ["black"] = 0x000000, ["blanchedalmond"] = 0xFFEBCD, ["blue"] = 0x0000FF,
            ["blueviolet"] = 0x8A2BE2, ["brown"] = 0xA52A2A, ["burlywood"] = 0xDEB887, ["cadetblue"] = 0x5F9EA0, ["chartreuse"] = 0x7FFF00,
            ["chocolate"] = 0xD2691E, ["coral"] = 0xFF7F50, ["cornflowerblue"] = 0x6495ED, ["cornsilk"] = 0xFFF8DC, ["crimson"] = 0xDC143C,
            ["cyan"] = 0x00FFFF, ["darkblue"] = 0x00008B, ["darkcyan"] = 0x008B8B, ["darkgoldenrod"] = 0xB8860B, ["darkgray"] = 0xA9A9A9,
            ["darkgreen"] = 0x006400, ["darkgrey"] = 0xA9A9A9, ["darkkhaki"] = 0xBDB76B, ["darkmagenta"] = 0x8B008B, ["darkolivegreen"] = 0x556B2F,
            ["darkorange"] = 0xFF8C00, ["darkorchid"] = 0x9932CC, ["darkred"] = 0x8B0000, ["darksalmon"] = 0xE9967A, ["darkseagreen"] = 0x8FBC8F,
            ["darkslateblue"] = 0x483D8B, ["darkslategray"] = 0x2F4F4F, ["darkslategrey"] = 0x2F4F4F, ["darkturquoise"] = 0x00CED1, ["darkviolet"] = 0x9400D3,
            ["deeppink"] = 0xFF1493, ["deepskyblue"] = 0x00BFFF, ["dimgray"] = 0x696969, ["dimgrey"] = 0x696969, ["dodgerblue"] = 0x1E90FF,
            ["firebrick"] = 0xB22222, ["floralwhite"] = 0xFFFAF0, ["forestgreen"] = 0x228B22, ["fuchsia"] = 0xFF00FF, ["gainsboro"] = 0xDCDCDC,
            ["ghostwhite"] = 0xF8F8FF, ["gold"] = 0xFFD700, ["goldenrod"] = 0xDAA520, ["gray"] = 0x808080, ["green"] = 0x008000,
            ["greenyellow"] = 0xADFF2F, ["grey"] = 0x808080, ["honeydew"] = 0xF0FFF0, ["hotpink"] = 0xFF69B4, ["indianred"] = 0xCD5C5C,
            ["indigo"] = 0x4B0082, ["ivory"] = 0xFFFFF0, ["khaki"] = 0xF0E68C, ["lavender"] = 0xE6E6FA, ["lavenderblush"] = 0xFFF0F5,
            ["lawngreen"] = 0x7CFC00, ["lemonchiffon"] = 0xFFFACD, ["lightblue"] = 0xADD8E6, ["lightcoral"] = 0xF08080, ["lightcyan"] = 0xE0FFFF,
            ["lightgoldenrodyellow"] = 0xFAFAD2, ["lightgray"] = 0xD3D3D3, ["lightgreen"] = 0x90EE90, ["lightgrey"] = 0xD3D3D3, ["lightpink"] = 0xFFB6C1,
            ["lightsalmon"] = 0xFFA07A, ["lightseagreen"] = 0x20B2AA, ["lightskyblue"] = 0x87CEFA, ["lightslategray"] = 0x778899, ["lightslategrey"] = 0x778899,
            ["lightsteelblue"] = 0xB0C4DE, ["lightyellow"] = 0xFFFFE0, ["lime"] = 0x00FF00, ["limegreen"] = 0x32CD32, ["linen"] = 0xFAF0E6,
            ["magenta"] = 0xFF00FF, ["maroon"] = 0x800000, ["mediumaquamarine"] = 0x66CDAA, ["mediumblue"] = 0x0000CD, ["mediumorchid"] = 0xBA55D3,
            ["mediumpurple"] = 0x9370DB, ["mediumseagreen"] = 0x3CB371, ["mediumslateblue"] = 0x7B68EE, ["mediumspringgreen"] = 0x00FA9A, ["mediumturquoise"] = 0x48D1CC,
            ["mediumvioletred"] = 0xC71585, ["midnightblue"] = 0x191970, ["mintcream"] = 0xF5FFFA, ["mistyrose"] = 0xFFE4E1, ["moccasin"] = 0xFFE4B5,
            ["navajowhite"] = 0xFFDEAD, ["navy"] = 0x000080, ["oldlace"] = 0xFDF5E6, ["olive"] = 0x808000, ["olivedrab"] = 0x6B8E23,
            ["orange"] = 0xFFA500, ["orangered"] = 0xFF4500, ["orchid"] = 0xDA70D6, ["palegoldenrod"] = 0xEEE8AA, ["palegreen"] = 0x98FB98,
            ["paleturquoise"] = 0xAFEEEE, ["palevioletred"] = 0xDB7093, ["papayawhip"] = 0xFFEFD5, ["peachpuff"] = 0xFFDAB9, ["peru"] = 0xCD853F,
            ["pink"] = 0xFFC0CB, ["plum"] = 0xDDA0DD, ["powderblue"] = 0xB0E0E6, ["purple"] = 0x800080, ["rebeccapurple"] = 0x663399,
            ["red"] = 0xFF0000, ["rosybrown"] = 0xBC8F8F, ["royalblue"] = 0x4169E1, ["saddlebrown"] = 0x8B4513, ["salmon"] = 0xFA8072,
            ["sandybrown"] = 0xF4A460, ["seagreen"] = 0x2E8B57, ["seashell"] = 0xFFF5EE, ["sienna"] = 0xA0522D, ["silver"] = 0xC0C0C0,
            ["skyblue"] = 0x87CEEB, ["slateblue"] = 0x6A5ACD, ["slategray"] = 0x708090, ["slategrey"] = 0x708090, ["snow"] = 0xFFFAFA,
            ["springgreen"] = 0x00FF7F, ["steelblue"] = 0x4682B4, ["tan"] = 0xD2B48C, ["teal"] = 0x008080, ["thistle"] = 0xD8BFD8,
            ["tomato"] = 0xFF6347, ["turquoise"] = 0x40E0D0, ["violet"] = 0xEE82EE, ["wheat"] = 0xF5DEB3, ["white"] = 0xFFFFFF,
            ["whitesmoke"] = 0xF5F5F5, ["yellow"] = 0xFFFF00, ["yellowgreen"] = 0x9ACD32,
        };
    }
}
