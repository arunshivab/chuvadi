// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export
//        v2.1.1 — attribute-value escaping audit

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Builds an SVG document incrementally. Tracks open elements for proper closing.
/// </summary>
/// <remarks>
/// Caller-supplied string values are XML-escaped at every attribute boundary
/// so that font family lists like <c>Times, "Times New Roman", serif</c> (which
/// contain embedded double quotes) cannot break the output. Numeric values
/// produced via <see cref="F(double)"/> are guaranteed safe and are not
/// passed through the escaper. The <c>extraAttrs</c> overloads on
/// <see cref="OpenGroup"/> and <see cref="EmitPath"/> are intentionally
/// raw — they take a complete <c>name="value"</c> snippet rather than a
/// single value, and so trust the caller to have escaped each value already.
/// </remarks>
internal sealed class SvgWriter
{
    private readonly StringBuilder _body = new();
    private readonly StringBuilder _defs = new();
    private readonly StringBuilder _styles = new();
    private readonly Stack<string> _openElements = new();
    private readonly string _coordFormat;

    internal SvgWriter(int precision)
    {
        _coordFormat = "0." + new string('#', precision);
    }

    internal void StartSvg(double width, double height)
    {
        _body.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        _body.Append("xmlns:xlink=\"http://www.w3.org/1999/xlink\" ");
        _body.AppendFormat(CultureInfo.InvariantCulture,
            "width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">",
            F(width), F(height));
    }

    /// <summary>
    /// Emits the page-level coordinate flip (PDF bottom-left → SVG top-left)
    /// as an outer group. All subsequent content uses PDF-native coordinates.
    /// </summary>
    internal void OpenPageFlip(double pageHeight)
    {
        _body.AppendFormat(CultureInfo.InvariantCulture,
            "<g transform=\"matrix(1 0 0 -1 0 {0})\">", F(pageHeight));
        _openElements.Push("g");
    }

    internal void OpenGroup(string? transform = null, string? clipPathId = null,
        string? extraAttrs = null)
    {
        _body.Append("<g");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        if (clipPathId is not null)
        {
            _body.Append(" clip-path=\"url(#").Append(EscapeXml(clipPathId)).Append(")\"");
        }
        if (extraAttrs is not null) { _body.Append(' ').Append(extraAttrs); }
        _body.Append('>');
        _openElements.Push("g");
    }

    internal void CloseGroup()
    {
        if (_openElements.Count == 0 || _openElements.Peek() != "g")
        {
            throw new System.InvalidOperationException("Mismatched group close.");
        }
        _openElements.Pop();
        _body.Append("</g>");
    }

    internal void EmitPath(string d, string? fill, string? stroke,
        double strokeWidth, string? fillRule = null, string? extraAttrs = null)
    {
        _body.Append("<path d=\"").Append(EscapeXml(d)).Append('"');
        _body.Append(" fill=\"").Append(EscapeXml(fill ?? "none")).Append('"');
        if (stroke is not null)
        {
            _body.Append(" stroke=\"").Append(EscapeXml(stroke)).Append('"');
            _body.AppendFormat(CultureInfo.InvariantCulture,
                " stroke-width=\"{0}\"", F(strokeWidth));
        }
        if (fillRule is not null)
        {
            _body.Append(" fill-rule=\"").Append(EscapeXml(fillRule)).Append('"');
        }
        if (extraAttrs is not null) { _body.Append(' ').Append(extraAttrs); }
        _body.Append("/>");
    }

    internal void EmitText(string content, double x, double y, string fontFamily,
        double fontSize, string fill, string? transform = null)
    {
        _body.Append("<text");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        _body.AppendFormat(CultureInfo.InvariantCulture,
            " x=\"{0}\" y=\"{1}\"", F(x), F(y));
        _body.Append(" font-family=\"").Append(EscapeXml(fontFamily)).Append('"');
        _body.AppendFormat(CultureInfo.InvariantCulture,
            " font-size=\"{0}\"", F(fontSize));
        _body.Append(" fill=\"").Append(EscapeXml(fill)).Append('"');
        _body.Append('>').Append(EscapeXml(content)).Append("</text>");
    }

    internal void EmitImage(string href, double x, double y, double width, double height,
        string? transform = null)
    {
        _body.Append("<image");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        _body.AppendFormat(CultureInfo.InvariantCulture,
            " x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\"",
            F(x), F(y), F(width), F(height));
        _body.Append(" xlink:href=\"").Append(EscapeXml(href)).Append("\"/>");
    }

    /// <summary>Adds a clipPath definition; returns the assigned id.</summary>
    internal string AddClipPath(string pathData, string fillRule = "nonzero")
    {
        string id = $"clip{_defs.Length:X}_{pathData.GetHashCode():X}";
        _defs.Append("<clipPath id=\"").Append(id).Append("\">");
        _defs.Append("<path d=\"").Append(EscapeXml(pathData)).Append("\" clip-rule=\"")
            .Append(EscapeXml(fillRule)).Append("\"/></clipPath>");
        return id;
    }

    /// <summary>Adds an <c>@font-face</c> rule.</summary>
    /// <remarks>
    /// The font family and format strings are escaped for CSS — embedded
    /// double quotes are backslash-escaped per CSS string syntax. The data
    /// URL is emitted raw; data URLs are constrained to a safe character
    /// set by their producer (base64 + scheme) and contain neither quotes
    /// nor parentheses.
    /// </remarks>
    internal void AddFontFace(string family, string dataUrl, string format)
    {
        _styles.Append("@font-face{font-family:\"").Append(EscapeCssString(family))
            .Append("\";src:url(").Append(dataUrl).Append(") format(\"")
            .Append(EscapeCssString(format)).Append("\");}");
    }

    internal string ToSvgString()
    {
        StringBuilder result = new();
        // Output: open svg, then <defs> (with style + clip-paths), then body, then close.
        result.Append(_body.ToString());
        // Insert <defs> after the opening <svg ...>. Look for the first '>'.
        string built = result.ToString();
        int firstClose = built.IndexOf('>');
        StringBuilder finished = new();
        finished.Append(built, 0, firstClose + 1);
        if (_defs.Length > 0 || _styles.Length > 0)
        {
            finished.Append("<defs>");
            if (_styles.Length > 0)
            {
                finished.Append("<style type=\"text/css\">").Append(_styles).Append("</style>");
            }
            finished.Append(_defs);
            finished.Append("</defs>");
        }
        finished.Append(built, firstClose + 1, built.Length - firstClose - 1);
        // Close any still-open groups (defensively).
        while (_openElements.Count > 0)
        {
            string el = _openElements.Pop();
            finished.Append("</").Append(el).Append('>');
        }
        finished.Append("</svg>");
        return finished.ToString();
    }

    /// <summary>Formats a coordinate with configured precision.</summary>
    internal string F(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) { return "0"; }
        return v.ToString(_coordFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes a string for inclusion in either XML text content or a
    /// double-quoted XML attribute value. Control characters other than
    /// tab, newline, and carriage return are replaced with a space so the
    /// result is always well-formed XML 1.0.
    /// </summary>
    private static string EscapeXml(string text)
    {
        StringBuilder sb = new(text.Length + 8);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default:
                    if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r')
                    {
                        sb.Append(' ');   // Control char → space.
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a string for inclusion in a CSS double-quoted string literal.
    /// Backslashes and double quotes are escaped with a leading backslash
    /// per CSS Syntax Module Level 3 §4.3.5.
    /// </summary>
    private static string EscapeCssString(string text)
    {
        StringBuilder sb = new(text.Length + 8);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\A "); break;
                case '\r': sb.Append("\\D "); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
