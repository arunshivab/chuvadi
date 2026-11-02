using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using Chuvadi.Markup;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Layout.Markup
{
    /// <summary>
    /// Builds Chuvadi.Layout's flow document from HTML, so HTML mail and pages print and export to PDF like any document.
    /// Untrusted HTML is first sanitized with Chuvadi.Markup's <see cref="HtmlSanitizer"/> (by default with a new
    /// <see cref="SanitizerPolicy"/>, Chuvadi.Markup's safest policy); the sanitized DOM is then laid out. Supported: headings, paragraphs,
    /// <c>div</c> and the other block elements, line breaks, bold, italic, underline, strike, sub/superscript, small/big,
    /// code and <c>pre</c>, <c>mark</c>, <c>q</c>, links, block quotes, ordered, unordered and definition lists (types,
    /// start, value, reversed), tables (thead/tbody/tfoot, col/rowspan, widths, cellpadding, cellspacing, border, bgcolor,
    /// alignment, nested tables), horizontal rules, <c>data:</c> images (sizes and max-width), and inline CSS: colours,
    /// backgrounds, fonts, sizes, weights, styles, decorations, letter spacing, text-align, text-indent, line-height,
    /// white-space, margins, padding, borders, widths, display, visibility and page breaks; plus the presentational
    /// attributes of mail HTML (align, bgcolor, <c>&lt;font&gt;</c>, width, height, valign). Style sheets
    /// (<c>&lt;style&gt;</c>), floats and positioning are not applied, with a warning. Text is never altered. Nothing is
    /// ever fetched.
    /// </summary>
    public static class HtmlImporter
    {
        /// <summary>Sanitizes HTML text with <see cref="HtmlImportOptions.Policy"/> and builds the flow document.</summary>
        /// <exception cref="LayoutLimitExceededException">The input exceeds a Chuvadi.Markup or <see cref="LayoutLimits"/> limit.</exception>
        public static FlowDocument Import(string html, HtmlImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            options ??= new HtmlImportOptions();
            layoutOptions ??= new LayoutOptions();
            Limits.Check(nameof(LayoutLimits.MaxSourceBytes), layoutOptions.Limits.MaxSourceBytes, html.Length);
            SanitizeResult sanitized = Markup(() => HtmlSanitizer.Sanitize(html, options.Policy ?? new SanitizerPolicy(), cancellationToken));
            return Import(sanitized, options, layoutOptions, warning, cancellationToken);
        }

        /// <summary>
        /// Sanitizes encoded HTML (a file, or a MIME part) and builds the flow document. The encoding comes from
        /// <see cref="HtmlImportOptions.Encoding"/>, else the byte-order mark, a <c>&lt;meta&gt;</c> charset, then windows-1252.
        /// </summary>
        public static FlowDocument Import(byte[] html, HtmlImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            options ??= new HtmlImportOptions();
            layoutOptions ??= new LayoutOptions();
            Limits.Check(nameof(LayoutLimits.MaxSourceBytes), layoutOptions.Limits.MaxSourceBytes, html.LongLength);
            SanitizeResult sanitized = Markup(() => HtmlSanitizer.Sanitize(html, options.Policy ?? new SanitizerPolicy(), options.Encoding, cancellationToken));
            return Import(sanitized, options, layoutOptions, warning, cancellationToken);
        }

        /// <summary>
        /// Builds the flow document from HTML already sanitized (for example the same <see cref="SanitizeResult"/> a mail
        /// client shows, with its <c>cid:</c> parts resolved). The title and blocked-resource report are used.
        /// </summary>
        public static FlowDocument Import(SanitizeResult sanitized, HtmlImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sanitized);
            HtmlImport import = new HtmlImport(options ?? new HtmlImportOptions(), layoutOptions ?? new LayoutOptions(), warning ?? (_ => { }), cancellationToken);
            import.Report(sanitized);
            return import.Run(sanitized.Fragment, sanitized.Title);
        }

        /// <summary>
        /// Builds the flow document from a DOM as it is (an <see cref="HtmlDocument"/>, a fragment or an element), without
        /// sanitizing it. Scripts, style sheets and embedded content are never run or fetched; use this for HTML you trust
        /// or have already sanitized.
        /// </summary>
        public static FlowDocument Import(HtmlNode root, HtmlImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(root);
            HtmlImport import = new HtmlImport(options ?? new HtmlImportOptions(), layoutOptions ?? new LayoutOptions(), warning ?? (_ => { }), cancellationToken);
            HtmlNode start = root is HtmlDocument d ? (HtmlNode?)d.Body ?? (HtmlNode?)d.DocumentElement ?? d : root;
            return import.Run(start, root is HtmlDocument doc ? doc.Title : string.Empty);
        }

        private static T Markup<T>(Func<T> run)
        {
            try
            {
                return run();
            }
            catch (MarkupLimitExceededException ex)
            {
                throw new LayoutLimitExceededException("Markup" + ex.LimitName, ex.Limit, ex.Actual);
            }
        }
    }

    /// <summary>One HTML → flow conversion.</summary>
    internal sealed partial class HtmlImport
    {
        private readonly HtmlImportOptions _o;
        private readonly HtmlStyleSheet _s;
        private readonly LayoutOptions _lo;
        private readonly Shared.Warnings _warn;
        private readonly CancellationToken _ct;
        private long _nodes;
        private string? _firstH1;
        private int _missingImages;

        public HtmlImport(HtmlImportOptions options, LayoutOptions layoutOptions, Action<LayoutWarning> warn, CancellationToken ct)
        {
            _o = options;
            _s = options.Styles ?? HtmlStyleSheet.Default;
            _lo = layoutOptions;
            _warn = new Shared.Warnings(warn);
            _ct = ct;
        }

        /// <summary>A list in progress: its kind and next number.</summary>
        private sealed class ListState
        {
            public bool Ordered;
            public int Next = 1;
            public bool Reversed;
            public double Hang;
            public int Depth;
        }

        /// <summary>Where blocks go: a section body or a table cell, with its width and the spacing waiting to be applied.</summary>
        private sealed class Container
        {
            public Container(IList<FlowBlock> blocks, double width, bool isBody)
            {
                Blocks = blocks;
                Width = width;
                IsBody = isBody;
            }

            public IList<FlowBlock> Blocks { get; }
            public double Width { get; }
            public bool IsBody { get; }
            public double Pending;
            public bool BreakNext;
            public FlowParagraph? Para;
            public bool LastSpace = true;
            public ListLabel? Label;
            public double LabelHang;
            public Ctx? LabelCtx;
        }

        /// <summary>Block geometry and inherited style where content is placed.</summary>
        private sealed record Ctx(HtmlStyle Style, double Left, double Right, BorderLine BarLeft, BorderLine BarRight, LayoutColor? Background,
            ListState? List, int ListDepth, int TableDepth);

        public void Report(SanitizeResult r)
        {
            int images = 0;
            foreach (BlockedResource b in r.BlockedResources)
            {
                if (b.Kind == ResourceKind.Image)
                {
                    images++;
                }
            }
            if (images > 0)
            {
                _warn.Add(LayoutWarningCodes.ImagePlaceholder, LayoutWarningSeverity.ContentLoss,
                    images.ToString(CultureInfo.InvariantCulture) + " image(s) were blocked by the sanitizer policy (remote or unresolved sources); their alternative text is printed.");
            }
            foreach (string w in r.Warnings)
            {
                _warn.Add(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Sanitizer: " + w);
            }
        }

        public FlowDocument Run(HtmlNode root, string? title)
        {
            PageGeometry page = _o.Page ?? PaperDefaults.PageForCulture(_lo.Culture);
            FlowSection section = new FlowSection { Page = page };
            Container body = new Container(section.Blocks, page.ContentWidth, true);
            HtmlStyle rootStyle = Root();
            Ctx ctx = new Ctx(rootStyle, 0, 0, BorderLine.None, BorderLine.None, null, null, 0, 0);
            Walk(root, ctx, body);
            EndParagraph(body);
            if (_missingImages > 0)
            {
                _warn.Add(LayoutWarningCodes.ImagePlaceholder, LayoutWarningSeverity.ContentLoss,
                    "Some images had no data (blocked, not a data: URI, or no ImageResolver result); " +
                    (_o.MissingImages == MissingImageHandling.AltText ? "their alternative text is printed." : "they were left out."));
            }
            if (_o.PageNumbers)
            {
                CharacterFormat f = rootStyle.Char with { FontSize = _s.FooterSize, Color = _s.MutedColor };
                section.HeadersFooters = new HeaderFooterSet { DefaultFooter = new List<FlowBlock> { Shared.PageFooter(_o.PageNumberText, f) } };
            }
            string? t = _o.Title ?? (string.IsNullOrWhiteSpace(title) ? null : title) ?? _firstH1 ?? _lo.DocumentName;
            FlowDocument doc = new FlowDocument { Title = t };
            doc.Sections.Add(section);
            return doc;
        }

        // ---- Walking ----

        private void Walk(HtmlNode node, Ctx c, Container k)
        {
            for (HtmlNode? child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                _ct.ThrowIfCancellationRequested();
                Limits.Check(nameof(LayoutLimits.MaxBlocks), _lo.Limits.MaxBlocks, ++_nodes);
                switch (child)
                {
                    case HtmlText t:
                        Text(t.Data, c, k);
                        break;
                    case HtmlElement e:
                        Element(e, c, k);
                        break;
                }
            }
        }

        private void Element(HtmlElement e, Ctx c, Container k)
        {
            if (e.Namespace != HtmlNamespace.Html)
            {
                _warn.Add(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss, "SVG and MathML are not laid out; they were left out.");
                return;
            }
            string tag = e.LocalName;
            if (s_skipped.Contains(tag))
            {
                if (tag == "style")
                {
                    _warn.Add(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity,
                        "Style sheets (<style>) are not applied in this version; inline styles and presentational attributes are.");
                }
                return;
            }
            if (tag is "input" or "select" or "textarea" or "audio" or "video" or "canvas" or "object" or "embed"
                or "progress" or "meter" or "output" or "image")
            {
                // A button's caption is ordinary text and is kept.
                _warn.Add(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss, "Form controls, audio, video and embedded objects are not laid out; they were left out.");
                return;
            }
            double avail = Math.Max(18, k.Width - c.Left - c.Right);
            HtmlStyle s = Style(e, c.Style, avail);
            if (s.Hidden || s.Display == HtmlDisplay.None || (_zeroHeight.Contains(s) && IsOverflowHidden(e)))
            {
                return;
            }
            switch (tag)
            {
                case "br":
                    LineBreak(c with { Style = s }, k);
                    return;
                case "img":
                    Image(e, s, c, k);
                    return;
                case "hr":
                    Rule(e, s, c, k);
                    return;
                case "table":
                    Table(e, s, c, k);
                    return;
                case "q" when s.Display == HtmlDisplay.Inline:
                {
                    Ctx qc = c with { Style = s };
                    int level = c.Style.QuoteDepth % 2;
                    IReadOnlyList<string> quotes = _s.Quotes;
                    string open = quotes.Count > (level * 2) + 1 ? quotes[level * 2] : "\u201C";
                    string close = quotes.Count > (level * 2) + 1 ? quotes[(level * 2) + 1] : "\u201D";
                    Text(open, qc, k);
                    Walk(e, qc, k);
                    Text(close, qc, k);
                    return;
                }
            }
            if (tag == "h1" && _firstH1 is null)
            {
                string h = Collapse(e.TextContent).Trim();
                _firstH1 = h.Length > 0 ? h : null;
            }
            if (s.Display == HtmlDisplay.Inline)
            {
                Walk(e, c with { Style = s }, k);
                return;
            }
            Block(e, s, c, k);
        }

        private static bool IsOverflowHidden(HtmlElement e)
        {
            string? style = e.GetAttribute("style");
            return style is not null && style.Replace(" ", string.Empty).Contains("overflow:hidden", StringComparison.OrdinalIgnoreCase);
        }

        private void Block(HtmlElement e, HtmlStyle s, Ctx c, Container k) =>
            BlockWith(e, s, c, k, null);

        /// <summary>Lays out a block box: margins, borders, padding and background around its content (its children, or <paramref name="content"/>).</summary>
        private void BlockWith(HtmlElement e, HtmlStyle s, Ctx c, Container k, Action<Ctx, Container>? content)
        {
            EndParagraph(k);
            if (s.BreakBefore)
            {
                k.BreakNext = true;
            }
            BorderLine top = s.BorderTop, bottom = s.BorderBottom, left = s.BorderLeft, right = s.BorderRight;
            double padTop = s.PaddingTop ?? 0, padBottom = s.PaddingBottom ?? 0, padLeft = s.PaddingLeft ?? 0, padRight = s.PaddingRight ?? 0;
            k.Pending = Math.Max(k.Pending, s.MarginTop) + (top.IsVisible ? 0 : padTop);
            int first = k.Blocks.Count;
            ListState? list = c.List;
            int listDepth = c.ListDepth;
            string tag = e.LocalName;
            if (tag is "ul" or "ol" or "menu" or "dir")
            {
                list = new ListState
                {
                    Ordered = tag == "ol",
                    Reversed = tag == "ol" && e.HasAttribute("reversed"),
                    Hang = Math.Max(0, s.MarginLeft + padLeft),
                    Depth = c.ListDepth,
                };
                if (list.Ordered)
                {
                    int count = 0;
                    foreach (HtmlElement li in e.Children)
                    {
                        if (li.LocalName == "li")
                        {
                            count++;
                        }
                    }
                    list.Next = int.TryParse(e.GetAttribute("start")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ? start : list.Reversed ? count : 1;
                }
                listDepth = c.ListDepth + 1;
            }
            Ctx inner = c with
            {
                Style = s,
                Left = c.Left + s.MarginLeft + (left.IsVisible ? left.Width : 0) + padLeft,
                Right = c.Right + s.MarginRight + (right.IsVisible ? right.Width : 0) + padRight,
                BarLeft = left.IsVisible ? left with { Space = padLeft } : c.BarLeft,
                BarRight = right.IsVisible ? right with { Space = padRight } : c.BarRight,
                Background = _o.PrintBackgrounds && s.Background is LayoutColor bg ? bg : c.Background,
                List = list,
                ListDepth = listDepth,
            };
            if (content is not null)
            {
                content(inner, k);
            }
            else if (s.Display == HtmlDisplay.ListItem)
            {
                ListItem(e, s, c, inner, k);
            }
            else
            {
                Walk(e, inner, k);
            }
            EndParagraph(k);
            if (k.Blocks.Count == first)
            {
                // An empty block still takes its height (spacer divs in mail).
                if (TryHeight(s, out double h))
                {
                    k.Pending += h;
                }
            }
            else
            {
                FlowParagraph? firstP = k.Blocks[first] as FlowParagraph;
                FlowParagraph? lastP = k.Blocks[^1] as FlowParagraph;
                if (top.IsVisible && firstP is not null)
                {
                    firstP.Format = firstP.Format with { BorderTop = top with { Space = padTop } };
                }
                if (bottom.IsVisible && lastP is not null)
                {
                    lastP.Format = lastP.Format with { BorderBottom = bottom with { Space = padBottom } };
                }
                if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" && lastP is not null)
                {
                    lastP.Format = lastP.Format with { KeepWithNext = true, KeepLinesTogether = true, StyleId = "Heading" + tag[1] };
                }
                if (s.AvoidInside)
                {
                    for (int i = first; i < k.Blocks.Count; i++)
                    {
                        if (k.Blocks[i] is FlowParagraph p)
                        {
                            p.Format = p.Format with { KeepLinesTogether = true, KeepWithNext = i < k.Blocks.Count - 1 || p.Format.KeepWithNext };
                        }
                    }
                }
            }
            k.Pending = Math.Max(k.Pending, s.MarginBottom) + (bottom.IsVisible ? 0 : padBottom);
            if (s.BreakAfter)
            {
                k.BreakNext = true;
            }
        }

        private bool TryHeight(HtmlStyle s, out double h)
        {
            h = 0;
            return s.Height is string v && HtmlCss.TryLength(v, s.Char.FontSize, _s.BodySize, null, out h) && h > 0;
        }

        // ---- Lists ----

        private void ListItem(HtmlElement e, HtmlStyle s, Ctx outer, Ctx inner, Container k)
        {
            FlushLabel(k);
            ListState list = outer.List ?? new ListState { Hang = _s.Indent };
            if (int.TryParse(e.GetAttribute("value")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                list.Next = value;
            }
            string? type = s.ListStyle;
            string? text;
            if (type is null)
            {
                IReadOnlyList<string> bullets = _s.Bullets.Count > 0 ? _s.Bullets : new[] { "\u2022" };
                text = list.Ordered ? Number(list.Next, "decimal") : bullets[Math.Max(0, list.Depth) % bullets.Count];
            }
            else
            {
                text = type switch
                {
                    "none" => null,
                    "disc" => "\u2022",
                    "circle" => "\u25E6",
                    "square" => "\u25AA",
                    _ => list.Ordered || IsOrderedType(type) ? Number(list.Next, type) : "\u2022",
                };
            }
            list.Next += list.Reversed ? -1 : 1;
            if (text is not null)
            {
                CharacterFormat lf = s.Char with { Underline = UnderlineStyle.None, Strikethrough = false, Background = null, VerticalAlignment = VerticalTextAlignment.Baseline };
                k.Label = new ListLabel(text, lf);
                k.LabelHang = list.Hang;
                k.LabelCtx = inner;
            }
            Walk(e, inner, k);
            if (k.Label is not null)
            {
                // An empty item still shows its marker.
                Current(inner, k);
            }
        }

        /// <summary>A marker still waiting for its item's first paragraph gets a paragraph of its own (before a nested list or a table).</summary>
        private void FlushLabel(Container k)
        {
            if (k.Label is not null && k.LabelCtx is Ctx lc)
            {
                Current(lc, k);
                EndParagraph(k);
            }
            k.Label = null;
        }

        private static bool IsOrderedType(string t) =>
            t is "decimal" or "decimal-leading-zero" or "lower-alpha" or "lower-latin" or "upper-alpha" or "upper-latin"
                or "lower-roman" or "upper-roman" or "lower-greek";

        private static string Number(int n, string type)
        {
            string body = type switch
            {
                "decimal-leading-zero" => n is >= 0 and < 10 ? "0" + n.ToString(CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture),
                "lower-alpha" or "lower-latin" => n > 0 ? Alpha(n).ToLowerInvariant() : n.ToString(CultureInfo.InvariantCulture),
                "upper-alpha" or "upper-latin" => n > 0 ? Alpha(n) : n.ToString(CultureInfo.InvariantCulture),
                "lower-roman" => n is > 0 and < 4000 ? Roman(n).ToLowerInvariant() : n.ToString(CultureInfo.InvariantCulture),
                "upper-roman" => n is > 0 and < 4000 ? Roman(n) : n.ToString(CultureInfo.InvariantCulture),
                "lower-greek" => n > 0 ? Greek(n) : n.ToString(CultureInfo.InvariantCulture),
                _ => n.ToString(CultureInfo.InvariantCulture),
            };
            return body + ".";
        }

        private static string Alpha(int n)
        {
            StringBuilder sb = new StringBuilder();
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)('A' + (n % 26)));
                n /= 26;
            }
            return sb.ToString();
        }

        private static string Greek(int n)
        {
            const string letters = "\u03B1\u03B2\u03B3\u03B4\u03B5\u03B6\u03B7\u03B8\u03B9\u03BA\u03BB\u03BC\u03BD\u03BE\u03BF\u03C0\u03C1\u03C3\u03C4\u03C5\u03C6\u03C7\u03C8\u03C9";
            StringBuilder sb = new StringBuilder();
            while (n > 0)
            {
                n--;
                sb.Insert(0, letters[n % letters.Length]);
                n /= letters.Length;
            }
            return sb.ToString();
        }

        private static string Roman(int n)
        {
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] r = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                while (n >= v[i])
                {
                    sb.Append(r[i]);
                    n -= v[i];
                }
            }
            return sb.ToString();
        }

        // ---- Paragraphs and inline content ----

        /// <summary>Indents stop growing at half the width, so deep nesting still leaves room for text.</summary>
        private static double Cap(double indent, double width) => Math.Clamp(indent, 0, Math.Max(0, width / 2));

        private ParagraphFormat Format(Ctx c, Container k)
        {
            HtmlStyle s = c.Style;
            (LineSpacingRule rule, double value) = s.LineKind switch
            {
                LineHeightKind.Factor => (LineSpacingRule.AtLeast, s.LineValue * s.Char.FontSize),
                LineHeightKind.Absolute => (LineSpacingRule.AtLeast, s.LineValue),
                _ => (LineSpacingRule.Multiple, 1.0),
            };
            if (rule == LineSpacingRule.AtLeast && value <= 0)
            {
                (rule, value) = (LineSpacingRule.Multiple, 1.0);
            }
            return new ParagraphFormat
            {
                Alignment = s.Align,
                LeftIndent = Cap(c.Left, k.Width),
                RightIndent = Cap(c.Right, k.Width),
                FirstLineIndent = s.TextIndent,
                LineSpacingRule = rule,
                LineSpacing = value,
                Background = c.Background,
                BorderLeft = c.BarLeft,
                BorderRight = c.BarRight,
            };
        }

        /// <summary>The paragraph being filled, created (with the pending spacing, break and list label) when content arrives.</summary>
        private FlowParagraph Current(Ctx c, Container k)
        {
            if (k.Para is FlowParagraph open)
            {
                return open;
            }
            CharacterFormat mark = c.Style.Char with { Underline = UnderlineStyle.None, Strikethrough = false, Background = null, VerticalAlignment = VerticalTextAlignment.Baseline };
            if (mark.FontSize <= 0)
            {
                mark = mark with { FontSize = 1 };
            }
            FlowParagraph p = new FlowParagraph { Format = Format(c, k), MarkFormat = mark };
            ApplyPending(p, k);
            if (k.Label is ListLabel label)
            {
                double hang = Math.Min(Math.Max(k.LabelHang, 0), p.Format.LeftIndent);
                // Centred or right-aligned items keep the marker next to their text, as browsers do.
                if (hang >= 12 && p.Format.Alignment is not (ParagraphAlignment.Center or ParagraphAlignment.Right))
                {
                    p.Format = p.Format with { FirstLineIndent = -hang };
                    p.ListLabel = label;
                }
                else
                {
                    p.ListLabel = label with { Suffix = ListLabelSuffix.Space };
                }
                k.Label = null;
            }
            k.Blocks.Add(p);
            k.Para = p;
            k.LastSpace = true;
            return p;
        }

        /// <summary>Collapsed margins: the larger of the previous paragraph's space after and the pending space.</summary>
        private static void ApplyPending(FlowParagraph p, Container k)
        {
            if (k.Blocks.Count == 0 && k.IsBody)
            {
                // The top of the first page: browsers print without the body's leading margin.
            }
            else if (k.Blocks.Count > 0 && k.Blocks[^1] is FlowParagraph prev)
            {
                prev.Format = prev.Format with { SpaceAfter = Math.Max(prev.Format.SpaceAfter, k.Pending) };
            }
            else
            {
                p.Format = p.Format with { SpaceBefore = k.Pending };
            }
            k.Pending = 0;
            if (k.BreakNext)
            {
                p.Format = p.Format with { PageBreakBefore = true };
                k.BreakNext = false;
            }
        }

        private void EndParagraph(Container k)
        {
            if (k.Para is FlowParagraph p && k.LastSpace)
            {
                TrimTrailingSpace(p);
            }
            k.Para = null;
            k.LastSpace = true;
        }

        private static void TrimTrailingSpace(FlowParagraph p)
        {
            if (p.Inlines.Count > 0 && p.Inlines[^1] is TextRun t && t.Text.EndsWith(' '))
            {
                string trimmed = t.Text.TrimEnd(' ');
                p.Inlines.RemoveAt(p.Inlines.Count - 1);
                if (trimmed.Length > 0)
                {
                    p.Inlines.Add(new TextRun(trimmed, t.Format) { HyperlinkUri = t.HyperlinkUri });
                }
            }
        }

        private static string Collapse(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (char ch in text)
            {
                if (ch is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    if (!space)
                    {
                        sb.Append(' ');
                        space = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    space = false;
                }
            }
            return sb.ToString();
        }

        private void Text(string data, Ctx c, Container k)
        {
            HtmlStyle s = c.Style;
            if (s.Char.FontSize <= 0 || data.Length == 0)
            {
                // font-size: 0 hides text (mail uses it around images); images are still drawn.
                return;
            }
            string text;
            if (s.Preserves)
            {
                text = data;
            }
            else if (s.WhiteSpace == WhiteSpaceMode.PreLine)
            {
                string[] lines = data.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = Collapse(lines[i]).Trim(' ');
                }
                text = string.Join("\n", lines);
            }
            else
            {
                text = Collapse(data);
                if (k.LastSpace && text.StartsWith(' '))
                {
                    text = text.Substring(1);
                }
            }
            if (text.Length == 0)
            {
                return;
            }
            // Creating the paragraph resets the collapsing state, so the state is set after it.
            Current(c, k).Inlines.Add(new TextRun(text, s.Char) { HyperlinkUri = s.Link });
            k.LastSpace = s.Preserves || s.WhiteSpace == WhiteSpaceMode.PreLine ? text.EndsWith('\n') : text.EndsWith(' ');
        }

        private void LineBreak(Ctx c, Container k)
        {
            FlowParagraph p = Current(c, k);
            if (k.LastSpace)
            {
                TrimTrailingSpace(p);
            }
            CharacterFormat f = c.Style.Char.FontSize > 0 ? c.Style.Char : c.Style.Char with { FontSize = 1 };
            p.Inlines.Add(new BreakInline(BreakKind.Line, f with { Underline = UnderlineStyle.None, Strikethrough = false }));
            k.LastSpace = true;
        }

        private void Rule(HtmlElement e, HtmlStyle s, Ctx c, Container k)
        {
            EndParagraph(k);
            double em = s.Char.FontSize;
            k.Pending = Math.Max(k.Pending, Math.Max(s.MarginTop, em / 2));
            LayoutColor color = _s.RuleColor;
            if (s.BorderTop.IsVisible)
            {
                color = s.BorderTop.Color;
            }
            else if (HtmlCss.TryLegacyColor(e.GetAttribute("color"), out LayoutColor ac))
            {
                color = ac;
            }
            else if (s.Background is LayoutColor bg)
            {
                color = bg;
            }
            double weight = 0.75;
            if (HtmlCss.TryAttributeLength(e.GetAttribute("size"), null, out double size, out _) && size > 0)
            {
                weight = Math.Min(size, 72);
            }
            else if (s.BorderTop.IsVisible)
            {
                weight = s.BorderTop.Width;
            }
            double avail = Math.Max(18, k.Width - c.Left - c.Right);
            double left = c.Left, right = c.Right;
            if (s.Width is string w && HtmlCss.TryLength(w, em, _s.BodySize, avail, out double width) && width > 0 && width < avail)
            {
                double free = avail - width;
                ParagraphAlignment a = (e.GetAttribute("align") ?? string.Empty).Trim().ToLowerInvariant() switch
                {
                    "left" => ParagraphAlignment.Left,
                    "right" => ParagraphAlignment.Right,
                    _ => ParagraphAlignment.Center,
                };
                left += a == ParagraphAlignment.Left ? 0 : a == ParagraphAlignment.Right ? free : free / 2;
                right += a == ParagraphAlignment.Right ? 0 : a == ParagraphAlignment.Left ? free : free / 2;
            }
            FlowParagraph p = new FlowParagraph
            {
                Format = Format(c, k) with
                {
                    LeftIndent = Cap(left, k.Width),
                    RightIndent = Cap(right, k.Width),
                    FirstLineIndent = 0,
                    LineSpacingRule = LineSpacingRule.Exact,
                    LineSpacing = 1,
                    BorderBottom = new BorderLine(weight, color),
                },
                MarkFormat = s.Char with { FontSize = 1 },
            };
            ApplyPending(p, k);
            k.Blocks.Add(p);
            k.Pending = Math.Max(s.MarginBottom, em / 2);
        }

        // ---- Images ----

        /// <summary>The decoded image of an <c>img</c> element, or null.</summary>
        private readonly Dictionary<HtmlElement, LayoutImage?> _images = new Dictionary<HtmlElement, LayoutImage?>(ReferenceEqualityComparer.Instance);

        private LayoutImage? ImageData(HtmlElement e)
        {
            if (_images.TryGetValue(e, out LayoutImage? cached))
            {
                return cached;
            }
            string src = (e.GetAttribute("src") ?? string.Empty).Trim();
            byte[]? bytes = null;
            if (src.Length > 0)
            {
                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    if (_o.DataUriImages)
                    {
                        bytes = Shared.DataUri(src, _lo.Limits);
                    }
                }
                else if (_o.ImageResolver is Func<string, byte[]?> resolve)
                {
                    bytes = resolve(src);
                }
            }
            LayoutImage? image = null;
            if (bytes is { Length: > 0 })
            {
                Limits.Check(nameof(LayoutLimits.MaxResourceBytes), _lo.Limits.MaxResourceBytes, bytes.LongLength);
                image = LayoutImage.FromEncoded(bytes);
            }
            _images[e] = image;
            return image;
        }

        /// <summary>The printed size of an image in points, within <paramref name="avail"/>.</summary>
        private (double W, double H) ImageSize(HtmlElement e, HtmlStyle s, LayoutImage? image, double avail)
        {
            double em = s.Char.FontSize;
            double dpi = _o.ImageDpi > 0 ? _o.ImageDpi : 96;
            double iw = image is { PixelWidth: > 0 } ? image.PixelWidth * 72 / dpi : 0;
            double ih = image is { PixelHeight: > 0 } ? image.PixelHeight * 72 / dpi : 0;
            double? w = s.Width is string sw && HtmlCss.TryLength(sw, em, _s.BodySize, avail, out double wv) && wv >= 0 ? wv : null;
            double? h = s.Height is string sh && HtmlCss.TryLength(sh, em, _s.BodySize, null, out double hv) && hv >= 0 ? hv : null;
            double width, height;
            if (w is double ww && h is double hh)
            {
                (width, height) = (ww, hh);
            }
            else if (w is double w1)
            {
                (width, height) = (w1, iw > 0 ? w1 * ih / iw : w1 * 0.75);
            }
            else if (h is double h1)
            {
                (width, height) = (ih > 0 ? h1 * iw / ih : h1 / 0.75, h1);
            }
            else if (iw > 0 && ih > 0)
            {
                (width, height) = (iw, ih);
            }
            else
            {
                (width, height) = (image is null ? 0 : 144, image is null ? 0 : 108);
            }
            double max = avail;
            if (s.MaxWidth is string mw && HtmlCss.TryLength(mw, em, _s.BodySize, avail, out double mwv) && mwv >= 0)
            {
                max = Math.Min(max, mwv);
            }
            if (width > max && width > 0)
            {
                height *= max / width;
                width = max;
            }
            return (width, height);
        }

        private void Image(HtmlElement e, HtmlStyle s, Ctx c, Container k)
        {
            LayoutImage? image = ImageData(e);
            string alt = e.GetAttribute("alt") ?? string.Empty;
            bool block = s.Display != HtmlDisplay.Inline;
            Ctx ic = c with { Style = s };
            if (block)
            {
                EndParagraph(k);
                k.Pending = Math.Max(k.Pending, s.MarginTop);
                if (s.MarginLeftAuto && s.MarginRightAuto)
                {
                    HtmlStyle centred = s.Inherit();
                    centred.Align = ParagraphAlignment.Center;
                    ic = c with { Style = centred };
                }
            }
            if (image is null)
            {
                _missingImages++;
                if (_o.MissingImages == MissingImageHandling.AltText && alt.Trim().Length > 0)
                {
                    HtmlStyle muted = s.Inherit();
                    muted.Char = s.Char with { Italic = true, Color = _s.MutedColor };
                    Text(alt, ic with { Style = muted }, k);
                }
            }
            else
            {
                double avail = Math.Max(18, k.Width - c.Left - c.Right);
                (double w, double h) = ImageSize(e, s, image, avail);
                if (w > 0 && h > 0)
                {
                    FlowParagraph p = Current(ic, k);
                    CharacterFormat f = s.Char.FontSize > 0 ? s.Char : s.Char with { FontSize = 1 };
                    p.Inlines.Add(new ImageInline(image, w, h, f with { Underline = UnderlineStyle.None }) { Description = alt });
                    k.LastSpace = false;
                }
            }
            if (block)
            {
                EndParagraph(k);
                k.Pending = Math.Max(k.Pending, s.MarginBottom);
            }
        }
    }
}
