using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Markup;
using Chuvadi.Markup.Css;
using Chuvadi.Markup.Html;

namespace Chuvadi.Layout.Markup
{
    internal enum HtmlDisplay
    {
        Inline,
        Block,
        ListItem,
        Table,
        None,
    }

    internal enum WhiteSpaceMode
    {
        Normal,
        NoWrap,
        Pre,
        PreWrap,
        PreLine,
    }

    internal enum LineHeightKind
    {
        Normal,
        Factor,
        Absolute,
    }

    /// <summary>The computed style of one element: inherited properties plus its own box.</summary>
    internal sealed class HtmlStyle
    {
        // Inherited.
        public CharacterFormat Char = CharacterFormat.Default;
        public ParagraphAlignment Align;
        public double TextIndent;
        public LineHeightKind LineKind;
        public double LineValue;
        public WhiteSpaceMode WhiteSpace;
        public string? ListStyle;
        public string? Link;
        public bool LegacyCenter;
        public int QuoteDepth;

        // Not inherited.
        public HtmlDisplay Display = HtmlDisplay.Inline;
        public bool Hidden;
        public double MarginTop, MarginRight, MarginBottom, MarginLeft;
        public bool MarginLeftAuto, MarginRightAuto;
        public double? PaddingTop, PaddingRight, PaddingBottom, PaddingLeft;
        // Border sides: 0 top, 1 right, 2 bottom, 3 left. Width defaults to medium; no style means no border.
        public readonly double[] BorderWidth = { 2.25, 2.25, 2.25, 2.25 };
        public readonly string?[] BorderStyle = new string?[4];
        public readonly LayoutColor?[] BorderColor = new LayoutColor?[4];

        public BorderLine BorderTop => Side(0);
        public BorderLine BorderRight => Side(1);
        public BorderLine BorderBottom => Side(2);
        public BorderLine BorderLeft => Side(3);

        private BorderLine Side(int i)
        {
            string? style = BorderStyle[i];
            double width = BorderWidth[i];
            if (style is null or "none" or "hidden" || width <= 0)
            {
                return BorderLine.None;
            }
            return new BorderLine(width, BorderColor[i] ?? Char.Color, style switch
            {
                "dotted" => LineDash.Dot,
                "dashed" => LineDash.Dash,
                _ => LineDash.Solid,
            }, 0, style == "double");
        }
        public LayoutColor? Background;
        public string? Width, Height, MaxWidth;
        public CellVerticalAlignment? VAlign;
        public bool BreakBefore, BreakAfter, AvoidInside;

        public HtmlStyle Inherit() => new HtmlStyle
        {
            Char = Char,
            Align = Align,
            TextIndent = TextIndent,
            LineKind = LineKind,
            LineValue = LineValue,
            WhiteSpace = WhiteSpace,
            ListStyle = ListStyle,
            Link = Link,
            LegacyCenter = LegacyCenter,
            QuoteDepth = QuoteDepth,
        };

        /// <summary>A copy with the same inherited and box properties.</summary>
        public HtmlStyle Clone()
        {
            HtmlStyle c = Inherit();
            c.Display = Display;
            c.Hidden = Hidden;
            (c.MarginTop, c.MarginRight, c.MarginBottom, c.MarginLeft) = (MarginTop, MarginRight, MarginBottom, MarginLeft);
            (c.MarginLeftAuto, c.MarginRightAuto) = (MarginLeftAuto, MarginRightAuto);
            (c.PaddingTop, c.PaddingRight, c.PaddingBottom, c.PaddingLeft) = (PaddingTop, PaddingRight, PaddingBottom, PaddingLeft);
            Array.Copy(BorderWidth, c.BorderWidth, 4);
            Array.Copy(BorderStyle, c.BorderStyle, 4);
            Array.Copy(BorderColor, c.BorderColor, 4);
            c.Background = Background;
            (c.Width, c.Height, c.MaxWidth) = (Width, Height, MaxWidth);
            c.VAlign = VAlign;
            (c.BreakBefore, c.BreakAfter, c.AvoidInside) = (BreakBefore, BreakAfter, AvoidInside);
            return c;
        }

        public bool Preserves => WhiteSpace is WhiteSpaceMode.Pre or WhiteSpaceMode.PreWrap;
    }

    internal sealed partial class HtmlImport
    {
        private readonly Dictionary<HtmlElement, HtmlStyle> _styles = new Dictionary<HtmlElement, HtmlStyle>(ReferenceEqualityComparer.Instance);

        private static readonly HashSet<string> s_blocks = new HashSet<string>(StringComparer.Ordinal)
        {
            "address", "article", "aside", "blockquote", "body", "center", "dd", "details", "dialog", "dir", "div", "dl", "dt",
            "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup",
            "hr", "html", "legend", "listing", "main", "menu", "nav", "ol", "p", "plaintext", "pre", "search", "section",
            "summary", "ul", "xmp", "caption", "optgroup",
        };

        private static readonly HashSet<string> s_skipped = new HashSet<string>(StringComparer.Ordinal)
        {
            "head", "script", "style", "template", "title", "meta", "link", "base", "noscript", "noembed", "noframes",
            "rp", "param", "source", "track", "area", "col", "colgroup", "datalist", "frameset", "frame", "iframe",
        };

        private HtmlStyle Root()
        {
            HtmlStyleSheet s = _s;
            return new HtmlStyle
            {
                Char = new CharacterFormat { FontFamily = s.BodyFont, GenericFamily = s.BodyGeneric, FontSize = s.BodySize, Color = s.TextColor },
                Display = HtmlDisplay.Block,
            };
        }

        /// <summary>The computed style of <paramref name="e"/> (cached; an element always has the same parent).</summary>
        private HtmlStyle Style(HtmlElement e, HtmlStyle parent, double containerWidth)
        {
            if (_styles.TryGetValue(e, out HtmlStyle? cached))
            {
                return cached;
            }
            HtmlStyle st = parent.Inherit();
            string tag = e.LocalName;
            st.Display = s_blocks.Contains(tag) ? HtmlDisplay.Block
                : tag == "li" ? HtmlDisplay.ListItem
                : tag == "table" ? HtmlDisplay.Table
                : HtmlDisplay.Inline;
            if (e.HasAttribute("hidden"))
            {
                st.Hidden = true;
            }

            List<(string Name, string Value)> ua = UserAgent(e, parent);
            List<(string Name, string Value)> attrs = Presentational(e);
            List<(string Name, string Value)> normal = new List<(string, string)>();
            List<(string Name, string Value)> important = new List<(string, string)>();
            string? inline = e.GetAttribute("style");
            if (!string.IsNullOrWhiteSpace(inline))
            {
                try
                {
                    foreach (CssDeclaration d in CssParser.ParseDeclarations(inline, null, _ct))
                    {
                        (d.Important ? important : normal).Add((d.Name.ToLowerInvariant(), d.ValueText));
                    }
                }
                catch (MarkupLimitExceededException)
                {
                    // An over-long style attribute is ignored, as a browser ignores what it cannot parse.
                }
            }

            // Font size first: em lengths in the same element use it.
            double size = parent.Char.FontSize;
            foreach (List<(string Name, string Value)> group in new[] { ua, attrs, normal, important })
            {
                foreach ((string name, string value) in group)
                {
                    if (name == "font-size" && HtmlCss.FontSize(value, parent.Char.FontSize, _s.BodySize) is double fs)
                    {
                        size = fs;
                    }
                    else if (name == "font")
                    {
                        foreach (string t in HtmlCss.Tokens(value.Split('/')[0]))
                        {
                            if (HtmlCss.FontSize(t, parent.Char.FontSize, _s.BodySize) is double fs2 && (char.IsDigit(t[0]) || t[0] == '.' || t.Contains("small") || t.Contains("large") || t == "medium"))
                            {
                                size = fs2;
                            }
                        }
                    }
                }
            }
            st.Char = st.Char with { FontSize = size };

            foreach (List<(string Name, string Value)> group in new[] { ua, attrs, normal, important })
            {
                foreach ((string name, string value) in group)
                {
                    Apply(st, parent, name, value.Trim(), containerWidth);
                }
            }
            if (tag == "a")
            {
                string? href = e.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href) && _o.InertLinkAttribute is string inert)
                {
                    href = e.GetAttribute(inert);
                }
                if (!string.IsNullOrWhiteSpace(href))
                {
                    st.Link = _o.LinkResolver is Func<string, string?> resolve ? resolve(href.Trim()) : Shared.DefaultLink(href);
                }
            }
            else if (tag == "q")
            {
                st.QuoteDepth = parent.QuoteDepth + 1;
            }
            if (st.Display == HtmlDisplay.Inline && st.Background is LayoutColor inlineBg && _o.PrintBackgrounds)
            {
                // An inline element's background is behind its text.
                st.Char = st.Char with { Background = inlineBg };
            }
            _styles[e] = st;
            return st;
        }

        /// <summary>The browser's built-in style for an element, as declarations.</summary>
        private List<(string, string)> UserAgent(HtmlElement e, HtmlStyle parent)
        {
            List<(string, string)> d = new List<(string, string)>();
            string I(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            string mono = I(_s.MonospaceScale * 100) + "%";
            switch (e.LocalName)
            {
                case "b":
                case "strong":
                    d.Add(("font-weight", "bold"));
                    break;
                case "i":
                case "em":
                case "cite":
                case "var":
                case "dfn":
                    d.Add(("font-style", "italic"));
                    break;
                case "address":
                    d.Add(("font-style", "italic"));
                    break;
                case "u":
                case "ins":
                    d.Add(("text-decoration", "underline"));
                    break;
                case "s":
                case "strike":
                case "del":
                    d.Add(("text-decoration", "line-through"));
                    break;
                case "code":
                case "kbd":
                case "samp":
                case "tt":
                    d.Add(("font-family", "monospace"));
                    d.Add(("font-size", mono));
                    break;
                case "pre":
                case "listing":
                case "xmp":
                case "plaintext":
                    d.Add(("font-family", "monospace"));
                    d.Add(("font-size", mono));
                    d.Add(("white-space", "pre"));
                    d.Add(("margin", "1em 0"));
                    break;
                case "small":
                    d.Add(("font-size", "smaller"));
                    break;
                case "big":
                    d.Add(("font-size", "larger"));
                    break;
                case "sub":
                    // The engine draws sub/superscript at 2/3 size, so no font-size change here.
                    d.Add(("vertical-align", "sub"));
                    break;
                case "sup":
                    d.Add(("vertical-align", "super"));
                    break;
                case "mark":
                    d.Add(("background-color", Hex(_s.MarkBackground)));
                    d.Add(("color", "black"));
                    break;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                {
                    int level = e.LocalName[1] - '0';
                    double scale = _s.HeadingScales.Count >= level ? _s.HeadingScales[level - 1] : 1;
                    double margin = _s.HeadingMargins.Count >= level ? _s.HeadingMargins[level - 1] : 1;
                    d.Add(("font-size", I(scale) + "em"));
                    d.Add(("font-weight", "bold"));
                    d.Add(("margin", I(margin) + "em 0"));
                    break;
                }
                case "p":
                case "dl":
                    d.Add(("margin", "1em 0"));
                    break;
                case "blockquote":
                case "figure":
                    d.Add(("margin", "1em " + I(_s.Indent) + "pt"));
                    break;
                case "ul":
                case "ol":
                case "menu":
                case "dir":
                    d.Add(("margin", _listDepthForStyle(e) > 0 ? "0" : "1em 0"));
                    d.Add(("padding-left", I(_s.Indent) + "pt"));
                    break;
                case "dd":
                    d.Add(("margin-left", I(_s.Indent) + "pt"));
                    break;
                case "center":
                    d.Add(("text-align", "center"));
                    break;
                case "th":
                    d.Add(("font-weight", "bold"));
                    d.Add(("text-align", "center"));
                    break;
                case "caption":
                    d.Add(("text-align", "center"));
                    break;
                case "nobr":
                    d.Add(("white-space", "nowrap"));
                    break;
                case "a":
                    if (e.HasAttribute("href") || (_o.InertLinkAttribute is string inert && e.HasAttribute(inert)))
                    {
                        d.Add(("color", Hex(_s.LinkColor)));
                        if (_s.UnderlineLinks)
                        {
                            d.Add(("text-decoration", "underline"));
                        }
                    }
                    break;
                case "fieldset":
                    d.Add(("margin", "0 1.5pt"));
                    break;
            }
            return d;
        }

        /// <summary>Lists nested in list items lose their vertical margins (the browser rule for ul ul, ol ol, …).</summary>
        private static int _listDepthForStyle(HtmlElement e)
        {
            for (HtmlNode? p = e.Parent; p is not null; p = p.Parent)
            {
                if (p is HtmlElement pe && pe.Namespace == HtmlNamespace.Html && pe.LocalName is "ul" or "ol" or "menu" or "dir")
                {
                    return 1;
                }
            }
            return 0;
        }

        private static string Hex(LayoutColor c) => "#" + c.R.ToString("x2", CultureInfo.InvariantCulture) + c.G.ToString("x2", CultureInfo.InvariantCulture) + c.B.ToString("x2", CultureInfo.InvariantCulture);

        /// <summary>Presentational attributes (align, bgcolor, font color/face/size, width, valign …) as declarations.</summary>
        private List<(string, string)> Presentational(HtmlElement e)
        {
            List<(string, string)> d = new List<(string, string)>();
            string tag = e.LocalName;
            if (e.GetAttribute("align") is string align)
            {
                string a = align.Trim().ToLowerInvariant();
                if (tag is "table")
                {
                    if (a == "center")
                    {
                        d.Add(("margin-left", "auto"));
                        d.Add(("margin-right", "auto"));
                    }
                }
                else if (tag is not "img" and not "hr" and not "iframe" and not "object" and not "input")
                {
                    if (a is "left" or "right" or "center" or "justify" or "middle")
                    {
                        d.Add(("text-align", a == "middle" ? "center" : a));
                        if (a is "center" or "middle")
                        {
                            d.Add(("-chuvadi-legacy-center", "1"));
                        }
                    }
                }
            }
            if (tag == "center")
            {
                d.Add(("-chuvadi-legacy-center", "1"));
            }
            if (e.GetAttribute("bgcolor") is string bg && HtmlCss.TryLegacyColor(bg, out LayoutColor bgc))
            {
                d.Add(("background-color", Hex(bgc)));
            }
            if (tag == "font")
            {
                if (e.GetAttribute("color") is string fc && HtmlCss.TryLegacyColor(fc, out LayoutColor fcc))
                {
                    d.Add(("color", Hex(fcc)));
                }
                if (e.GetAttribute("face") is string face && face.Trim().Length > 0)
                {
                    d.Add(("font-family", face));
                }
                if (HtmlCss.LegacyFontSize(e.GetAttribute("size"), _s.BodySize) is double fs)
                {
                    d.Add(("font-size", fs.ToString("0.###", CultureInfo.InvariantCulture) + "pt"));
                }
            }
            if (tag is "table" or "td" or "th" or "img" or "hr" or "col" or "pre")
            {
                if (Dimension(e.GetAttribute("width")) is string w)
                {
                    d.Add(("width", w));
                }
            }
            if (tag is "table" or "td" or "th" or "tr" or "img")
            {
                if (Dimension(e.GetAttribute("height")) is string h)
                {
                    d.Add(("height", h));
                }
            }
            if (e.GetAttribute("valign") is string valign)
            {
                d.Add(("vertical-align", valign.Trim().ToLowerInvariant()));
            }
            if (tag is "td" or "th" && e.HasAttribute("nowrap"))
            {
                d.Add(("white-space", "nowrap"));
            }
            if (tag is "ul" or "ol" or "li" && e.GetAttribute("type") is string type)
            {
                string? ls = type.Trim() switch
                {
                    "1" => "decimal",
                    "a" => "lower-alpha",
                    "A" => "upper-alpha",
                    "i" => "lower-roman",
                    "I" => "upper-roman",
                    _ => type.Trim().ToLowerInvariant() is "disc" or "circle" or "square" or "none" ? type.Trim().ToLowerInvariant() : null,
                };
                if (ls is not null)
                {
                    d.Add(("list-style-type", ls));
                }
            }
            return d;
        }

        /// <summary>An HTML dimension attribute as a CSS value ("600" → "600px").</summary>
        private static string? Dimension(string? value)
        {
            if (value is null)
            {
                return null;
            }
            string v = value.Trim();
            int end = 0;
            while (end < v.Length && (char.IsDigit(v[end]) || v[end] == '.'))
            {
                end++;
            }
            if (end == 0)
            {
                return null;
            }
            return end < v.Length && v[end] == '%' ? v.Substring(0, end) + "%" : v.Substring(0, end) + "px";
        }

        private void Apply(HtmlStyle st, HtmlStyle parent, string name, string value, double cw)
        {
            double em = st.Char.FontSize;
            string lower = value.ToLowerInvariant();
            if (lower is "inherit" or "initial" or "unset" or "revert")
            {
                return;
            }
            switch (name)
            {
                case "-chuvadi-legacy-center":
                    st.LegacyCenter = true;
                    break;
                case "display":
                    st.Display = lower switch
                    {
                        "none" => HtmlDisplay.None,
                        "block" or "flex" or "grid" or "flow-root" or "table-row" or "table-cell" or "table-row-group" or "table-caption" => st.Display == HtmlDisplay.Table ? HtmlDisplay.Table : HtmlDisplay.Block,
                        "list-item" => HtmlDisplay.ListItem,
                        "table" or "inline-table" => st.Display == HtmlDisplay.Table ? HtmlDisplay.Table : HtmlDisplay.Block,
                        "inline" or "inline-block" or "inline-flex" or "inline-grid" or "contents" => st.Display == HtmlDisplay.Table ? HtmlDisplay.Table : HtmlDisplay.Inline,
                        _ => st.Display,
                    };
                    break;
                case "visibility":
                    st.Hidden |= lower is "hidden" or "collapse";
                    break;
                case "mso-hide":
                    st.Hidden |= lower == "all";
                    break;
                case "opacity":
                    st.Hidden |= double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out double op) && op <= 0;
                    break;
                case "max-height":
                    if (HtmlCss.TryLength(lower, em, _s.BodySize, null, out double mh) && mh <= 0)
                    {
                        _zeroHeight.Add(st);
                    }
                    break;
                case "color":
                    if (HtmlCss.TryColor(value, parent.Char.Color, out LayoutColor color))
                    {
                        st.Char = st.Char with { Color = color };
                    }
                    break;
                case "background-color":
                case "background":
                    foreach (string t in name == "background" ? HtmlCss.Tokens(value) : new List<string> { value })
                    {
                        if (HtmlCss.TryColor(t, st.Char.Color, out LayoutColor bg))
                        {
                            st.Background = bg.A == 0 ? null : bg;
                        }
                    }
                    break;
                case "font-family":
                    FontFamily(st, value);
                    break;
                case "font":
                    Font(st, value);
                    break;
                case "font-weight":
                    Weight(st, parent, lower);
                    break;
                case "font-style":
                    st.Char = st.Char with { Italic = lower.StartsWith("italic", StringComparison.Ordinal) || lower.StartsWith("oblique", StringComparison.Ordinal) };
                    break;
                case "font-variant":
                case "font-variant-caps":
                    if (lower.Contains("small-caps"))
                    {
                        st.Char = st.Char with { Capitals = TextCapitals.SmallCaps };
                    }
                    else if (lower == "normal" && st.Char.Capitals == TextCapitals.SmallCaps)
                    {
                        st.Char = st.Char with { Capitals = TextCapitals.None };
                    }
                    break;
                case "text-transform":
                    if (lower == "uppercase")
                    {
                        st.Char = st.Char with { Capitals = TextCapitals.AllCaps };
                    }
                    else if (lower == "none" && st.Char.Capitals == TextCapitals.AllCaps)
                    {
                        st.Char = st.Char with { Capitals = TextCapitals.None };
                    }
                    break;
                case "text-decoration":
                case "text-decoration-line":
                {
                    List<string> t = HtmlCss.Tokens(lower);
                    if (t.Contains("none"))
                    {
                        st.Char = st.Char with { Underline = UnderlineStyle.None, Strikethrough = false };
                    }
                    if (t.Contains("underline"))
                    {
                        st.Char = st.Char with { Underline = UnderlineStyle.Single };
                    }
                    if (t.Contains("line-through"))
                    {
                        st.Char = st.Char with { Strikethrough = true };
                    }
                    foreach (string c in t)
                    {
                        if (name == "text-decoration" && HtmlCss.TryColor(c, st.Char.Color, out LayoutColor dc) && c != "none")
                        {
                            st.Char = st.Char with { UnderlineColor = dc };
                        }
                    }
                    break;
                }
                case "vertical-align":
                    switch (lower)
                    {
                        case "super":
                            st.Char = st.Char with { VerticalAlignment = VerticalTextAlignment.Superscript };
                            break;
                        case "sub":
                            st.Char = st.Char with { VerticalAlignment = VerticalTextAlignment.Subscript };
                            break;
                        case "baseline":
                            st.Char = st.Char with { VerticalAlignment = VerticalTextAlignment.Baseline };
                            break;
                        case "top":
                        case "text-top":
                            st.VAlign = CellVerticalAlignment.Top;
                            break;
                        case "middle":
                        case "center":
                            st.VAlign = CellVerticalAlignment.Center;
                            break;
                        case "bottom":
                        case "text-bottom":
                            st.VAlign = CellVerticalAlignment.Bottom;
                            break;
                    }
                    break;
                case "letter-spacing":
                    if (lower == "normal")
                    {
                        st.Char = st.Char with { CharacterSpacing = 0 };
                    }
                    else if (HtmlCss.TryLength(lower, em, _s.BodySize, null, out double ls))
                    {
                        st.Char = st.Char with { CharacterSpacing = ls };
                    }
                    break;
                case "text-align":
                    st.Align = lower switch
                    {
                        "left" or "start" or "-webkit-left" or "-moz-left" => ParagraphAlignment.Left,
                        "right" or "end" or "-webkit-right" or "-moz-right" => ParagraphAlignment.Right,
                        "center" or "-webkit-center" or "-moz-center" => ParagraphAlignment.Center,
                        "justify" => ParagraphAlignment.Justify,
                        _ => st.Align,
                    };
                    if (lower is "-webkit-center" or "-moz-center")
                    {
                        st.LegacyCenter = true;
                    }
                    else if (lower is "left" or "right" or "start" or "end")
                    {
                        st.LegacyCenter = false;
                    }
                    break;
                case "text-indent":
                    if (HtmlCss.TryLength(lower, em, _s.BodySize, cw, out double ti))
                    {
                        st.TextIndent = ti;
                    }
                    break;
                case "line-height":
                    if (lower == "normal")
                    {
                        st.LineKind = LineHeightKind.Normal;
                    }
                    else if (double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) && factor >= 0)
                    {
                        st.LineKind = LineHeightKind.Factor;
                        st.LineValue = factor;
                    }
                    else if (HtmlCss.TryLength(lower, em, _s.BodySize, em, out double lh) && lh >= 0)
                    {
                        st.LineKind = LineHeightKind.Absolute;
                        st.LineValue = lh;
                    }
                    break;
                case "white-space":
                case "white-space-collapse":
                    st.WhiteSpace = lower switch
                    {
                        "normal" or "collapse" => WhiteSpaceMode.Normal,
                        "nowrap" => WhiteSpaceMode.NoWrap,
                        "pre" or "preserve" => WhiteSpaceMode.Pre,
                        "pre-wrap" or "break-spaces" or "preserve-spaces" => WhiteSpaceMode.PreWrap,
                        "pre-line" or "preserve-breaks" => WhiteSpaceMode.PreLine,
                        _ => st.WhiteSpace,
                    };
                    break;
                case "list-style-type":
                    st.ListStyle = lower;
                    break;
                case "list-style":
                    foreach (string t in HtmlCss.Tokens(lower))
                    {
                        if (t is not "inside" and not "outside" && !t.StartsWith("url(", StringComparison.Ordinal))
                        {
                            st.ListStyle = t;
                        }
                    }
                    break;
                case "margin":
                {
                    string[] box = Box(value);
                    Margin(st, "top", box[0], em, cw);
                    Margin(st, "right", box[1], em, cw);
                    Margin(st, "bottom", box[2], em, cw);
                    Margin(st, "left", box[3], em, cw);
                    break;
                }
                case "margin-top":
                case "margin-right":
                case "margin-bottom":
                case "margin-left":
                    Margin(st, name.Substring(7), value, em, cw);
                    break;
                case "padding":
                {
                    string[] box = Box(value);
                    Padding(st, "top", box[0], em, cw);
                    Padding(st, "right", box[1], em, cw);
                    Padding(st, "bottom", box[2], em, cw);
                    Padding(st, "left", box[3], em, cw);
                    break;
                }
                case "padding-top":
                case "padding-right":
                case "padding-bottom":
                case "padding-left":
                    Padding(st, name.Substring(8), value, em, cw);
                    break;
                case "border":
                case "border-top":
                case "border-right":
                case "border-bottom":
                case "border-left":
                {
                    int[] sides = name switch
                    {
                        "border-top" => new[] { 0 },
                        "border-right" => new[] { 1 },
                        "border-bottom" => new[] { 2 },
                        "border-left" => new[] { 3 },
                        _ => new[] { 0, 1, 2, 3 },
                    };
                    // A shorthand resets all three parts of its sides.
                    double bWidth = 3 * HtmlCss.PxToPt;
                    string? bStyle = null;
                    LayoutColor? bColor = null;
                    foreach (string t in HtmlCss.Tokens(value))
                    {
                        string l = t.ToLowerInvariant();
                        if (BorderWidthOf(l, em) is double w)
                        {
                            bWidth = w;
                        }
                        else if (IsBorderStyle(l))
                        {
                            bStyle = l;
                        }
                        else if (HtmlCss.TryColor(t, st.Char.Color, out LayoutColor c))
                        {
                            bColor = c;
                        }
                    }
                    foreach (int i in sides)
                    {
                        st.BorderWidth[i] = bWidth;
                        st.BorderStyle[i] = bStyle;
                        st.BorderColor[i] = bColor;
                    }
                    break;
                }
                case "border-width":
                case "border-style":
                case "border-color":
                {
                    string[] box = Box(value);
                    for (int i = 0; i < 4; i++)
                    {
                        BorderPart(st, i, name.Substring(7), box[i], em);
                    }
                    break;
                }
                case "border-top-width":
                case "border-top-style":
                case "border-top-color":
                    BorderPart(st, 0, name.Substring(11), value, em);
                    break;
                case "border-right-width":
                case "border-right-style":
                case "border-right-color":
                    BorderPart(st, 1, name.Substring(13), value, em);
                    break;
                case "border-bottom-width":
                case "border-bottom-style":
                case "border-bottom-color":
                    BorderPart(st, 2, name.Substring(14), value, em);
                    break;
                case "border-left-width":
                case "border-left-style":
                case "border-left-color":
                    BorderPart(st, 3, name.Substring(12), value, em);
                    break;
                case "width":
                    st.Width = lower;
                    break;
                case "height":
                    st.Height = lower;
                    break;
                case "max-width":
                    st.MaxWidth = lower;
                    break;
                case "page-break-before":
                case "break-before":
                    st.BreakBefore = lower is "always" or "page" or "left" or "right" or "recto" or "verso";
                    break;
                case "page-break-after":
                case "break-after":
                    st.BreakAfter = lower is "always" or "page" or "left" or "right" or "recto" or "verso";
                    break;
                case "page-break-inside":
                case "break-inside":
                    st.AvoidInside = lower is "avoid" or "avoid-page";
                    break;
            }
        }

        /// <summary>Elements with max-height 0 (with overflow hidden this hides them; mail uses it for preheaders).</summary>
        private readonly HashSet<HtmlStyle> _zeroHeight = new HashSet<HtmlStyle>(ReferenceEqualityComparer.Instance);

        private void FontFamily(HtmlStyle st, string value)
        {
            string? family = null;
            GenericFontFamily generic = GenericFontFamily.Unknown;
            foreach (string raw in value.Split(','))
            {
                string f = raw.Trim().Trim('"', '\'').Trim();
                if (f.Length == 0)
                {
                    continue;
                }
                GenericFontFamily g = f.ToLowerInvariant() switch
                {
                    "serif" => GenericFontFamily.Serif,
                    "sans-serif" or "system-ui" or "ui-sans-serif" or "-apple-system" or "blinkmacsystemfont" => GenericFontFamily.SansSerif,
                    "monospace" or "ui-monospace" => GenericFontFamily.Monospace,
                    _ => GenericFontFamily.Unknown,
                };
                if (g != GenericFontFamily.Unknown)
                {
                    if (generic == GenericFontFamily.Unknown)
                    {
                        generic = g;
                    }
                    continue;
                }
                family ??= f;
            }
            if (family is null && generic == GenericFontFamily.Unknown)
            {
                return;
            }
            family ??= generic switch
            {
                GenericFontFamily.Serif => _s.SerifFont,
                GenericFontFamily.Monospace => _s.MonospaceFont,
                _ => _s.SansSerifFont,
            };
            st.Char = st.Char with
            {
                FontFamily = family,
                HighAnsiFontFamily = null,
                GenericFamily = generic == GenericFontFamily.Unknown ? GenericFontFamily.Unknown : generic,
            };
        }

        private void Font(HtmlStyle st, string value)
        {
            // [style] [variant] [weight] size[/line-height] family; the size was taken in the first pass.
            List<string> tokens = HtmlCss.Tokens(value);
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i].ToLowerInvariant();
                if (t is "italic" or "oblique")
                {
                    st.Char = st.Char with { Italic = true };
                }
                else if (t is "bold" or "bolder" or "600" or "700" or "800" or "900")
                {
                    st.Char = st.Char with { Bold = true };
                }
                else if (t == "small-caps")
                {
                    st.Char = st.Char with { Capitals = TextCapitals.SmallCaps };
                }
                else if (char.IsDigit(t[0]) || t[0] == '.' || t.Contains("small") || t.Contains("large") || t == "medium")
                {
                    int slash = t.IndexOf('/');
                    if (slash > 0)
                    {
                        Apply(st, st, "line-height", t.Substring(slash + 1), 0);
                    }
                    if (i + 1 < tokens.Count)
                    {
                        FontFamily(st, string.Join(" ", tokens.GetRange(i + 1, tokens.Count - i - 1)));
                    }
                    return;
                }
            }
        }

        private static void Weight(HtmlStyle st, HtmlStyle parent, string v)
        {
            bool? bold = v switch
            {
                "bold" => true,
                "bolder" => true,
                "normal" => false,
                "lighter" => false,
                _ => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n >= 600 : null,
            };
            if (bold is bool b)
            {
                st.Char = st.Char with { Bold = b };
            }
        }

        /// <summary>The four sides of a box shorthand (top, right, bottom, left).</summary>
        private static string[] Box(string value)
        {
            List<string> t = HtmlCss.Tokens(value);
            return t.Count switch
            {
                0 => new[] { "", "", "", "" },
                1 => new[] { t[0], t[0], t[0], t[0] },
                2 => new[] { t[0], t[1], t[0], t[1] },
                3 => new[] { t[0], t[1], t[2], t[1] },
                _ => new[] { t[0], t[1], t[2], t[3] },
            };
        }

        private void Margin(HtmlStyle st, string side, string value, double em, double cw)
        {
            bool auto = value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
            if (!auto && !HtmlCss.TryLength(value, em, _s.BodySize, cw, out double v))
            {
                return;
            }
            double pt = auto ? 0 : HtmlCss.TryLength(value, em, _s.BodySize, cw, out double x) ? x : 0;
            switch (side)
            {
                case "top":
                    st.MarginTop = pt;
                    break;
                case "bottom":
                    st.MarginBottom = pt;
                    break;
                case "left":
                    st.MarginLeft = pt;
                    st.MarginLeftAuto = auto;
                    break;
                case "right":
                    st.MarginRight = pt;
                    st.MarginRightAuto = auto;
                    break;
            }
        }

        private void Padding(HtmlStyle st, string side, string value, double em, double cw)
        {
            if (!HtmlCss.TryLength(value, em, _s.BodySize, cw, out double pt) || pt < 0)
            {
                return;
            }
            switch (side)
            {
                case "top":
                    st.PaddingTop = pt;
                    break;
                case "bottom":
                    st.PaddingBottom = pt;
                    break;
                case "left":
                    st.PaddingLeft = pt;
                    break;
                case "right":
                    st.PaddingRight = pt;
                    break;
            }
        }

        private static void BorderPart(HtmlStyle st, int side, string part, string value, double em)
        {
            string l = value.Trim().ToLowerInvariant();
            switch (part)
            {
                case "width":
                    if (BorderWidthOf(l, em) is double w)
                    {
                        st.BorderWidth[side] = w;
                    }
                    break;
                case "style":
                    if (IsBorderStyle(l))
                    {
                        st.BorderStyle[side] = l;
                    }
                    break;
                case "color":
                    if (HtmlCss.TryColor(value, st.Char.Color, out LayoutColor c))
                    {
                        st.BorderColor[side] = c;
                    }
                    break;
            }
        }

        private static double? BorderWidthOf(string l, double em)
        {
            switch (l)
            {
                case "thin":
                    return 1 * HtmlCss.PxToPt;
                case "medium":
                    return 3 * HtmlCss.PxToPt;
                case "thick":
                    return 5 * HtmlCss.PxToPt;
            }
            return l.Length > 0 && (char.IsDigit(l[0]) || l[0] == '.') && HtmlCss.TryLength(l, em, 12, null, out double w) && w >= 0 ? w : null;
        }

        private static bool IsBorderStyle(string l) =>
            l is "none" or "hidden" or "solid" or "dotted" or "dashed" or "double" or "groove" or "ridge" or "inset" or "outset";
    }
}
