using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Chuvadi.Layout.Drawing;
using DocsThemeColor = Chuvadi.Docs.Model.ThemeColor;
using DocsThemeFont = Chuvadi.Docs.Model.ThemeFont;

namespace Chuvadi.Layout.Docs
{
    /// <summary>
    /// The colour and font schemes of a document's theme (word/theme/theme1.xml), for resolving theme colours, tints,
    /// shades and theme fonts. Without a theme part, the Office 2013–2022 default theme applies.
    /// </summary>
    internal sealed class DocsTheme
    {
        private readonly Dictionary<string, LayoutColor> _colors = new Dictionary<string, LayoutColor>(StringComparer.Ordinal)
        {
            ["dk1"] = new LayoutColor(0x00, 0x00, 0x00),
            ["lt1"] = new LayoutColor(0xFF, 0xFF, 0xFF),
            ["dk2"] = new LayoutColor(0x44, 0x54, 0x6A),
            ["lt2"] = new LayoutColor(0xE7, 0xE6, 0xE6),
            ["accent1"] = new LayoutColor(0x44, 0x72, 0xC4),
            ["accent2"] = new LayoutColor(0xED, 0x7D, 0x31),
            ["accent3"] = new LayoutColor(0xA5, 0xA5, 0xA5),
            ["accent4"] = new LayoutColor(0xFF, 0xC0, 0x00),
            ["accent5"] = new LayoutColor(0x5B, 0x9B, 0xD5),
            ["accent6"] = new LayoutColor(0x70, 0xAD, 0x47),
            ["hlink"] = new LayoutColor(0x05, 0x63, 0xC1),
            ["folHlink"] = new LayoutColor(0x95, 0x4F, 0x72),
        };

        private string _majorLatin = "Calibri Light";
        private string _minorLatin = "Calibri";
        private string? _majorEastAsia;
        private string? _minorEastAsia;
        private string? _majorComplex;
        private string? _minorComplex;

        public static DocsTheme Parse(byte[]? themeXml)
        {
            DocsTheme t = new DocsTheme();
            if (themeXml is null || themeXml.Length == 0)
            {
                return t;
            }
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                MaxCharactersInDocument = 16L * 1024 * 1024,
            };
            try
            {
                using XmlReader r = XmlReader.Create(new MemoryStream(themeXml, writable: false), settings);
                string? scheme = null;   // "clr", "major", "minor"
                string? slot = null;
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }
                    switch (r.LocalName)
                    {
                        case "clrScheme":
                            scheme = "clr";
                            break;
                        case "majorFont":
                            scheme = "major";
                            break;
                        case "minorFont":
                            scheme = "minor";
                            break;
                        case "dk1" or "lt1" or "dk2" or "lt2" or "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" or "hlink" or "folHlink" when scheme == "clr":
                            slot = r.LocalName;
                            break;
                        case "srgbClr" when scheme == "clr" && slot is not null:
                            if (TryHex(r.GetAttribute("val"), out LayoutColor c))
                            {
                                t._colors[slot] = c;
                            }
                            slot = null;
                            break;
                        case "sysClr" when scheme == "clr" && slot is not null:
                            if (TryHex(r.GetAttribute("lastClr"), out LayoutColor sc))
                            {
                                t._colors[slot] = sc;
                            }
                            slot = null;
                            break;
                        case "latin" when scheme is "major" or "minor":
                            if (r.GetAttribute("typeface") is { Length: > 0 } latin)
                            {
                                if (scheme == "major")
                                {
                                    t._majorLatin = latin;
                                }
                                else
                                {
                                    t._minorLatin = latin;
                                }
                            }
                            break;
                        case "ea" when scheme is "major" or "minor":
                            if (r.GetAttribute("typeface") is { Length: > 0 } ea)
                            {
                                if (scheme == "major")
                                {
                                    t._majorEastAsia = ea;
                                }
                                else
                                {
                                    t._minorEastAsia = ea;
                                }
                            }
                            break;
                        case "cs" when scheme is "major" or "minor":
                            if (r.GetAttribute("typeface") is { Length: > 0 } cs)
                            {
                                if (scheme == "major")
                                {
                                    t._majorComplex = cs;
                                }
                                else
                                {
                                    t._minorComplex = cs;
                                }
                            }
                            break;
                    }
                }
            }
            catch (XmlException)
            {
                // A malformed theme: keep the defaults (Word does the same).
            }
            return t;
        }

        /// <summary>The theme font for a slot; null when the theme leaves it empty (East Asian and complex-script slots often are).</summary>
        public string? Font(DocsThemeFont font) => font switch
        {
            DocsThemeFont.MajorAscii or DocsThemeFont.MajorHAnsi => _majorLatin,
            DocsThemeFont.MinorAscii or DocsThemeFont.MinorHAnsi => _minorLatin,
            DocsThemeFont.MajorEastAsia => _majorEastAsia,
            DocsThemeFont.MinorEastAsia => _minorEastAsia,
            DocsThemeFont.MajorBidi => _majorComplex,
            DocsThemeFont.MinorBidi => _minorComplex,
            _ => null,
        };

        /// <summary>A theme colour with Word's tint and shade (0–255; 255 = unchanged) applied to its HSL lightness.</summary>
        public LayoutColor Color(DocsThemeColor color, byte? tint = null, byte? shade = null)
        {
            string key = color switch
            {
                DocsThemeColor.Dark1 or DocsThemeColor.Text1 => "dk1",
                DocsThemeColor.Light1 or DocsThemeColor.Background1 => "lt1",
                DocsThemeColor.Dark2 or DocsThemeColor.Text2 => "dk2",
                DocsThemeColor.Light2 or DocsThemeColor.Background2 => "lt2",
                DocsThemeColor.Accent1 => "accent1",
                DocsThemeColor.Accent2 => "accent2",
                DocsThemeColor.Accent3 => "accent3",
                DocsThemeColor.Accent4 => "accent4",
                DocsThemeColor.Accent5 => "accent5",
                DocsThemeColor.Accent6 => "accent6",
                DocsThemeColor.Hyperlink => "hlink",
                _ => "folHlink",
            };
            LayoutColor c = _colors[key];
            if ((tint is null || tint == 255) && (shade is null || shade == 255))
            {
                return c;
            }
            (double h, double s, double l) = ToHsl(c);
            if (tint is byte t && t != 255)
            {
                l = (l * t / 255.0) + (1 - (t / 255.0));
            }
            if (shade is byte sh && sh != 255)
            {
                l *= sh / 255.0;
            }
            return FromHsl(h, s, Math.Clamp(l, 0, 1));
        }

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

        private static (double H, double S, double L) ToHsl(LayoutColor c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2;
            if (max == min)
            {
                return (0, 0, l);
            }
            double d = max - min;
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            return (h / 6, s, l);
        }

        private static LayoutColor FromHsl(double h, double s, double l)
        {
            if (s == 0)
            {
                byte v = (byte)Math.Round(l * 255);
                return new LayoutColor(v, v, v);
            }
            double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
            double p = (2 * l) - q;
            return new LayoutColor(Channel(p, q, h + (1.0 / 3)), Channel(p, q, h), Channel(p, q, h - (1.0 / 3)));
        }

        private static byte Channel(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }
            if (t > 1)
            {
                t -= 1;
            }
            double v = t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }
}
