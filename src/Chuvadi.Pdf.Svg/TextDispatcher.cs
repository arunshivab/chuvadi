// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9 — Text, §9.6 — Simple fonts, §9.7 — Composite fonts
// PHASE: Phase 2.0 — SVG export

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Svg;

internal static class TextDispatcher
{
    internal static void Dispatch(string op, List<PdfToken> operands, SvgGraphicsState s,
        SvgWriter w, PdfDictionary? resources, PdfDocument doc, SvgExportOptions opts,
        Dictionary<string, string> fontFamilyByKey, HashSet<string> emittedFontFaces)
    {
        switch (op)
        {
            case "BT":
                s.TextMatrix = Mat2x3.Identity;
                s.TextLineMatrix = Mat2x3.Identity;
                break;
            case "ET":
                break;
            case "Tf":
                if (operands.Count >= 2)
                {
                    string fontKey = operands[0].RawText.TrimStart('/');
                    double fontSize = Num(operands[1]);
                    s.FontName = fontKey;
                    s.FontSize = fontSize;
                    EnsureFontFamily(fontKey, resources, doc, opts, w, fontFamilyByKey, emittedFontFaces);
                }
                break;
            case "Td":
                if (operands.Count >= 2)
                {
                    Mat2x3 translate = new(1, 0, 0, 1, Num(operands[0]), Num(operands[1]));
                    Mat2x3 newLine = translate.Multiply(s.TextLineMatrix);
                    s.TextLineMatrix = newLine;
                    s.TextMatrix = newLine;
                }
                break;
            case "TD":
                if (operands.Count >= 2)
                {
                    double tx = Num(operands[0]);
                    double ty = Num(operands[1]);
                    s.Leading = -ty;
                    Mat2x3 translate = new(1, 0, 0, 1, tx, ty);
                    Mat2x3 newLine = translate.Multiply(s.TextLineMatrix);
                    s.TextLineMatrix = newLine;
                    s.TextMatrix = newLine;
                }
                break;
            case "Tm":
                if (operands.Count >= 6)
                {
                    Mat2x3 m = new(
                        Num(operands[0]), Num(operands[1]),
                        Num(operands[2]), Num(operands[3]),
                        Num(operands[4]), Num(operands[5]));
                    s.TextMatrix = m;
                    s.TextLineMatrix = m;
                }
                break;
            case "T*":
                {
                    Mat2x3 trans = new(1, 0, 0, 1, 0, -s.Leading);
                    Mat2x3 newLine = trans.Multiply(s.TextLineMatrix);
                    s.TextLineMatrix = newLine;
                    s.TextMatrix = newLine;
                }
                break;
            case "Tc": if (operands.Count > 0) { s.CharSpacing = Num(operands[0]); } break;
            case "Tw": if (operands.Count > 0) { s.WordSpacing = Num(operands[0]); } break;
            case "Tz": if (operands.Count > 0) { s.HorizontalScaling = Num(operands[0]); } break;
            case "TL": if (operands.Count > 0) { s.Leading = Num(operands[0]); } break;
            case "Tr": if (operands.Count > 0) { s.RenderingMode = (int)Num(operands[0]); } break;
            case "Ts": if (operands.Count > 0) { s.TextRise = Num(operands[0]); } break;

            case "Tj":
                if (operands.Count > 0)
                {
                    byte[] bytes = ExtractStringBytes(operands[0]);
                    EmitText(bytes, s, w, resources, doc, fontFamilyByKey);
                }
                break;
            case "'":
                {
                    Mat2x3 trans = new(1, 0, 0, 1, 0, -s.Leading);
                    Mat2x3 newLine = trans.Multiply(s.TextLineMatrix);
                    s.TextLineMatrix = newLine;
                    s.TextMatrix = newLine;
                    if (operands.Count > 0)
                    {
                        byte[] bytes = ExtractStringBytes(operands[0]);
                        EmitText(bytes, s, w, resources, doc, fontFamilyByKey);
                    }
                }
                break;
            case "\"":
                if (operands.Count >= 3)
                {
                    s.WordSpacing = Num(operands[0]);
                    s.CharSpacing = Num(operands[1]);
                    Mat2x3 trans = new(1, 0, 0, 1, 0, -s.Leading);
                    Mat2x3 newLine = trans.Multiply(s.TextLineMatrix);
                    s.TextLineMatrix = newLine;
                    s.TextMatrix = newLine;
                    byte[] bytes = ExtractStringBytes(operands[2]);
                    EmitText(bytes, s, w, resources, doc, fontFamilyByKey);
                }
                break;
            case "TJ":
                // Array of (string | number) tokens: numbers shift the text position.
                foreach (PdfToken tok in operands)
                {
                    if (tok.Type == PdfTokenType.LiteralString || tok.Type == PdfTokenType.HexString)
                    {
                        byte[] bytes = ExtractStringBytes(tok);
                        EmitText(bytes, s, w, resources, doc, fontFamilyByKey);
                    }
                }
                break;
        }
    }

    private static void EmitText(byte[] textBytes, SvgGraphicsState s, SvgWriter w,
        PdfDictionary? resources, PdfDocument doc, Dictionary<string, string> fontFamilyByKey)
    {
        if (textBytes.Length == 0) { return; }
        if (s.FontName is null) { return; }

        // Decode bytes to Unicode using the page's font.
        string decoded = DecodeWithFont(textBytes, s.FontName, resources, doc);
        if (string.IsNullOrEmpty(decoded)) { return; }

        // Position: text origin in user space is at TextMatrix * CTM applied to (0, 0).
        // But the outer page group already applies the bottom-left flip, so emit text
        // at the text-space origin and let the SVG group transform place it.
        Mat2x3 combined = s.TextMatrix.Multiply(s.Ctm);

        // For the text to read right-way-up despite the page flip, we counter-flip
        // the Y axis within this text element via a local matrix that inverts Y.
        Mat2x3 localFlip = new(1, 0, 0, -1, 0, 0);
        Mat2x3 textTransform = localFlip.Multiply(combined);
        string transformStr = textTransform.ToSvgMatrix("0.######");

        string family = fontFamilyByKey.GetValueOrDefault(s.FontName, "sans-serif");

        w.EmitText(decoded, 0, 0, family, s.FontSize, s.FillColor, transformStr);
    }

    private static string DecodeWithFont(byte[] textBytes, string fontKey,
        PdfDictionary? resources, PdfDocument doc)
    {
        if (resources is null) { return TryFallbackDecode(textBytes); }
        if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsVal))
        {
            return TryFallbackDecode(textBytes);
        }
        PdfDictionary? fontsDict = doc.Objects.ResolveAs<PdfDictionary>(fontsVal);
        if (fontsDict is null) { return TryFallbackDecode(textBytes); }
        if (!fontsDict.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fontVal))
        {
            return TryFallbackDecode(textBytes);
        }
        PdfDictionary? fontDict = doc.Objects.ResolveAs<PdfDictionary>(fontVal);
        if (fontDict is null) { return TryFallbackDecode(textBytes); }

        try
        {
            PdfFont font = PdfFont.FromDictionary(fontDict, doc.Objects);
            return font.Decode(textBytes);
        }
        catch
        {
            return TryFallbackDecode(textBytes);
        }
    }

    private static string TryFallbackDecode(byte[] bytes)
        => Encoding.Latin1.GetString(bytes);

    private static byte[] ExtractStringBytes(PdfToken token)
    {
        if (token.Type == PdfTokenType.LiteralString)
        {
            // Strip leading '(' and trailing ')'. Unescape PDF string escapes.
            return UnescapeLiteralString(token.RawBytes);
        }
        if (token.Type == PdfTokenType.HexString)
        {
            return UnescapeHexString(token.RawBytes);
        }
        return token.RawBytes;
    }

    private static byte[] UnescapeLiteralString(byte[] raw)
    {
        // raw includes the surrounding ( ). Strip them.
        int start = 0;
        int end = raw.Length;
        if (end > 0 && raw[0] == (byte)'(') { start = 1; }
        if (end > start && raw[end - 1] == (byte)')') { end--; }

        List<byte> result = new(end - start);
        for (int i = start; i < end; i++)
        {
            byte b = raw[i];
            if (b == (byte)'\\' && i + 1 < end)
            {
                byte next = raw[++i];
                switch (next)
                {
                    case (byte)'n': result.Add((byte)'\n'); break;
                    case (byte)'r': result.Add((byte)'\r'); break;
                    case (byte)'t': result.Add((byte)'\t'); break;
                    case (byte)'b': result.Add(0x08); break;
                    case (byte)'f': result.Add(0x0C); break;
                    case (byte)'(': result.Add((byte)'('); break;
                    case (byte)')': result.Add((byte)')'); break;
                    case (byte)'\\': result.Add((byte)'\\'); break;
                    default:
                        // Octal up to 3 digits.
                        if (next >= (byte)'0' && next <= (byte)'7')
                        {
                            int v = next - (byte)'0';
                            int digits = 1;
                            while (digits < 3 && i + 1 < end
                                && raw[i + 1] >= (byte)'0' && raw[i + 1] <= (byte)'7')
                            {
                                v = v * 8 + (raw[++i] - (byte)'0');
                                digits++;
                            }
                            result.Add((byte)v);
                        }
                        else { result.Add(next); }
                        break;
                }
            }
            else
            {
                result.Add(b);
            }
        }
        return result.ToArray();
    }

    private static byte[] UnescapeHexString(byte[] raw)
    {
        // raw includes the < >. Read hex pairs.
        int start = 0, end = raw.Length;
        if (end > 0 && raw[0] == (byte)'<') { start = 1; }
        if (end > start && raw[end - 1] == (byte)'>') { end--; }

        List<byte> result = new();
        int pending = -1;
        for (int i = start; i < end; i++)
        {
            byte b = raw[i];
            int v = HexValue(b);
            if (v < 0) { continue; }
            if (pending < 0) { pending = v; }
            else
            {
                result.Add((byte)((pending << 4) | v));
                pending = -1;
            }
        }
        if (pending >= 0) { result.Add((byte)(pending << 4)); }
        return result.ToArray();
    }

    private static int HexValue(byte b)
    {
        if (b >= (byte)'0' && b <= (byte)'9') { return b - (byte)'0'; }
        if (b >= (byte)'A' && b <= (byte)'F') { return 10 + b - (byte)'A'; }
        if (b >= (byte)'a' && b <= (byte)'f') { return 10 + b - (byte)'a'; }
        return -1;
    }

    private static double Num(PdfToken t)
        => double.Parse(t.RawText, NumberStyles.Float, CultureInfo.InvariantCulture);

    // ── Font family resolution + optional @font-face embedding ───────────

    private static void EnsureFontFamily(string fontKey, PdfDictionary? resources,
        PdfDocument doc, SvgExportOptions opts, SvgWriter w,
        Dictionary<string, string> fontFamilyByKey, HashSet<string> emittedFontFaces)
    {
        if (fontFamilyByKey.ContainsKey(fontKey)) { return; }

        // Resolve the font dictionary.
        PdfDictionary? fontDict = ResolveFontDict(fontKey, resources, doc);
        if (fontDict is null)
        {
            fontFamilyByKey[fontKey] = "sans-serif";
            return;
        }

        string baseFont = "Helvetica";
        if (fontDict.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? bfVal)
            && bfVal is PdfName bfName)
        {
            baseFont = bfName.Value;
            // Strip subset prefix "ABCDEF+".
            int plus = baseFont.IndexOf('+');
            if (plus >= 0 && plus < baseFont.Length - 1) { baseFont = baseFont[(plus + 1)..]; }
        }

        // Try to embed the font program if the strategy allows.
        if (opts.FontStrategy == SvgFontStrategy.EmbedAsWebFont)
        {
            string? embeddedFamily = TryEmbedFont(fontDict, baseFont, w, emittedFontFaces, doc);
            if (embeddedFamily is not null)
            {
                fontFamilyByKey[fontKey] = embeddedFamily;
                return;
            }
        }

        // Fall back to a CSS family for Standard 14 or generic.
        fontFamilyByKey[fontKey] = CssFamilyFor(baseFont);
    }

    private static PdfDictionary? ResolveFontDict(string fontKey, PdfDictionary? resources,
        PdfDocument doc)
    {
        if (resources is null) { return null; }
        if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsVal))
        {
            return null;
        }
        PdfDictionary? fontsDict = doc.Objects.ResolveAs<PdfDictionary>(fontsVal);
        if (fontsDict is null) { return null; }
        if (!fontsDict.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv)) { return null; }
        return doc.Objects.ResolveAs<PdfDictionary>(fv);
    }

    private static string CssFamilyFor(string baseFont)
    {
        if (baseFont.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase)
            || baseFont.StartsWith("Arial", StringComparison.OrdinalIgnoreCase))
        {
            return "Helvetica, Arial, sans-serif";
        }
        if (baseFont.StartsWith("Times", StringComparison.OrdinalIgnoreCase))
        {
            return "Times, \"Times New Roman\", serif";
        }
        if (baseFont.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
        {
            return "Courier, \"Courier New\", monospace";
        }
        if (baseFont.Equals("Symbol", StringComparison.OrdinalIgnoreCase)) { return "Symbol"; }
        if (baseFont.Equals("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
        {
            return "\"Zapf Dingbats\"";
        }
        return "sans-serif";
    }

    /// <summary>
    /// Tries to extract the embedded font program from /FontDescriptor and
    /// emit it as an <c>@font-face</c> rule. Returns the unique family name
    /// to use in <c>font-family</c>, or null if extraction failed.
    /// </summary>
    private static string? TryEmbedFont(PdfDictionary fontDict, string baseFont,
        SvgWriter w, HashSet<string> emittedFontFaces, PdfDocument doc)
    {
        // For composite (Type0) fonts, the FontDescriptor lives on the descendant.
        PdfDictionary? descriptor = ResolveFontDescriptor(fontDict, doc);
        if (descriptor is null) { return null; }

        // Choose font program: /FontFile2 (TrueType) > /FontFile3 (CFF/OpenType) > /FontFile (Type 1, unsupported).
        PdfStream? fontProgram = null;
        string? format = null;
        if (descriptor.TryGetValue(PdfName.Intern("FontFile2"), out PdfPrimitive? ff2)
            && doc.Objects.Resolve(ff2) is PdfStream ff2Stream)
        {
            fontProgram = ff2Stream;
            format = "truetype";
        }
        else if (descriptor.TryGetValue(PdfName.Intern("FontFile3"), out PdfPrimitive? ff3)
            && doc.Objects.Resolve(ff3) is PdfStream ff3Stream)
        {
            fontProgram = ff3Stream;
            // Could be Type 1C (CFF) or OpenType — check the stream's Subtype.
            if (ff3Stream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
                && sub is PdfName subName)
            {
                format = subName.Value == "OpenType" ? "opentype" : "truetype";
            }
            else { format = "opentype"; }
        }

        if (fontProgram is null || format is null) { return null; }

        byte[] fontBytes;
        try { fontBytes = StreamDecoder.Decode(fontProgram); }
        catch { return null; }
        if (fontBytes.Length == 0) { return null; }

        string family = $"chuvadi_{SanitizeFontName(baseFont)}_{fontBytes.Length:X}";
        if (!emittedFontFaces.Contains(family))
        {
            string dataUrl = $"data:font/{format};base64,{Convert.ToBase64String(fontBytes)}";
            w.AddFontFace(family, dataUrl, format);
            emittedFontFaces.Add(family);
        }
        return family;
    }

    private static PdfDictionary? ResolveFontDescriptor(PdfDictionary fontDict, PdfDocument doc)
    {
        // For Type0 (composite) fonts, descend into /DescendantFonts.
        if (fontDict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
            && sub is PdfName subName && subName.Value == "Type0"
            && fontDict.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? descVal))
        {
            PdfArray? descArr = doc.Objects.ResolveAs<PdfArray>(descVal);
            if (descArr is not null && descArr.Count > 0)
            {
                PdfDictionary? desc = doc.Objects.ResolveAs<PdfDictionary>(descArr[0]);
                if (desc is not null
                    && desc.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? dV))
                {
                    return doc.Objects.ResolveAs<PdfDictionary>(dV);
                }
            }
        }
        // Simple font: FontDescriptor is direct.
        if (fontDict.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdVal))
        {
            return doc.Objects.ResolveAs<PdfDictionary>(fdVal);
        }
        return null;
    }

    private static string SanitizeFontName(string name)
    {
        StringBuilder sb = new(name.Length);
        foreach (char ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        return sb.ToString();
    }
}
