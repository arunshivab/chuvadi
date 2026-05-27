// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: exercise the high-level IPdfReader facade end-to-end.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Reader;
using Chuvadi.Pdf.Rendering.DisplayList;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Reader <input.pdf> [search-query] [password]");
    Console.Error.WriteLine("                                 [--dump] [--all-pages]");
    Console.Error.WriteLine("                                 [--dump-line <keyword>]...");
    Console.Error.WriteLine("                                 [--dump-stream <page>]...");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Diagnostic flags:");
    Console.Error.WriteLine("  --dump              Legacy: dumps page 1 keyword + Job-line window.");
    Console.Error.WriteLine("  --dump-line <kw>    Scans every page. For each page, groups TextOps");
    Console.Error.WriteLine("                      by Y and concatenates each line in X order. The");
    Console.Error.WriteLine("                      first line containing <kw> as a substring is");
    Console.Error.WriteLine("                      dumped — every TextOp on that Y plus every");
    Console.Error.WriteLine("                      non-text op in the line window. Case-sensitive.");
    Console.Error.WriteLine("                      Repeat the flag to dump multiple lines.");
    Console.Error.WriteLine("  --dump-stream <p>   Dumps the raw PDF content stream operators for");
    Console.Error.WriteLine("                      page <p> (1-indexed). Each operator is printed");
    Console.Error.WriteLine("                      on its own line with a sequential counter and");
    Console.Error.WriteLine("                      its operands. Lets you see exactly what the PDF");
    Console.Error.WriteLine("                      contains independent of how DisplayListBuilder");
    Console.Error.WriteLine("                      interprets it. Repeat the flag for multiple pages.");
    Console.Error.WriteLine("  --all-pages         Render every page to page-N.svg.");
    return 1;
}

string inputPath = args[0];
bool legacyDump = Array.Exists(args, a => a == "--dump");
bool allPages = Array.Exists(args, a => a == "--all-pages");
List<string> dumpLineKeywords = ParseFlagArgs(args, "--dump-line");
List<int> dumpStreamPages = ParseFlagArgs(args, "--dump-stream")
    .Select(s => int.TryParse(s, out int n) ? n : -1)
    .Where(n => n > 0).ToList();
string[] positional = StripFlagValues(args);
string query = positional.Length > 1 ? positional[1] : "the";
string? password = positional.Length > 2 ? positional[2] : null;

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 1;
}

IPdfReader reader = new ChuvadiPdfReader();

await using FileStream input = File.OpenRead(inputPath);
string fileName = Path.GetFileName(inputPath);

using PdfDocument doc = await reader.OpenAsync(input, fileName, password);

Console.WriteLine($"File: {fileName}  ({doc.PageCount} page{(doc.PageCount == 1 ? string.Empty : "s")})");
Console.WriteLine();

if (doc.Encryption is null)
{
    Console.WriteLine("Encryption: none");
}
else
{
    Console.WriteLine($"Encryption: {doc.Encryption.Algorithm} ({doc.Encryption.KeyLength * 8}-bit, V={doc.Encryption.Version}, R={doc.Encryption.Revision})");
    Console.WriteLine($"  Permissions: print={doc.Encryption.AllowPrint}, modify={doc.Encryption.AllowModify}, copy={doc.Encryption.AllowCopy}, annotate={doc.Encryption.AllowAnnotate}");
}
Console.WriteLine();

Console.WriteLine("Metadata:");
Console.WriteLine($"  Title:        {doc.Title ?? "(none)"}");
Console.WriteLine($"  Author:       {doc.Author ?? "(none)"}");
Console.WriteLine($"  Subject:      {doc.Subject ?? "(none)"}");
Console.WriteLine($"  Creator:      {doc.Creator ?? "(none)"}");
Console.WriteLine($"  Producer:     {doc.Producer ?? "(none)"}");
Console.WriteLine($"  CreationDate: {doc.CreationDate?.ToString("o") ?? "(none)"}");
Console.WriteLine($"  ModDate:      {doc.ModDate?.ToString("o") ?? "(none)"}");
Console.WriteLine();

IReadOnlyList<OutlineItem> outlines = await reader.GetOutlinesAsync(doc);
Console.WriteLine($"Outline: {outlines.Count} top-level entr{(outlines.Count == 1 ? "y" : "ies")}");
if (outlines.Count > 0) { PrintOutline(outlines, depth: 1); }
Console.WriteLine();

Console.WriteLine($"Search \"{query}\":");
int matchCount = 0;
SearchOptions searchOptions = new SearchOptions();
await foreach (SearchMatch match in reader.SearchAsync(doc, query, searchOptions))
{
    matchCount++;
    if (matchCount <= 5)
    {
        string boxInfo = match.BoundingBoxes.Count > 0
            ? $" at ({match.BoundingBoxes[0].X:F0},{match.BoundingBoxes[0].Y:F0})"
            : string.Empty;
        Console.WriteLine($"  Page {match.PageNumber + 1}, char offset {match.CharacterOffset}{boxInfo}");
    }
}
if (matchCount == 0) { Console.WriteLine("  (no matches)"); }
else if (matchCount > 5) { Console.WriteLine($"  ... and {matchCount - 5} more (total: {matchCount})"); }
else { Console.WriteLine($"  (total: {matchCount})"); }
Console.WriteLine();

if (allPages)
{
    Console.WriteLine($"Render: all {doc.PageCount} pages → page-N.svg in {Environment.CurrentDirectory}");
    for (int pageIndex = 0; pageIndex < doc.PageCount; pageIndex++)
    {
        string pageSvg = await reader.RenderPageSvgAsync(doc, pageIndex);
        string pagePath = Path.Combine(Environment.CurrentDirectory, $"page-{pageIndex + 1}.svg");
        await File.WriteAllTextAsync(pagePath, pageSvg);
        Console.WriteLine($"  page-{pageIndex + 1}.svg  ({pageSvg.Length:N0} chars)");
    }
}
else
{
    string svg = await reader.RenderPageSvgAsync(doc, pageIndex: 0);
    string svgPath = Path.Combine(Environment.CurrentDirectory, "page-1.svg");
    await File.WriteAllTextAsync(svgPath, svg);
    Console.WriteLine($"Render: page 1 → {svgPath} ({svg.Length:N0} chars)");
}

IReadOnlyList<TextRun> runs = await reader.GetTextRunsAsync(doc, pageIndex: 0);
Console.WriteLine($"Text runs on page 1: {runs.Count}");

if (legacyDump)
{
    Console.WriteLine();
    Console.WriteLine("── Legacy dump: keyword TextOps + Job-line window on page 1 ──");
    string[] keywords = { "Dr", "ARUN", "Special", "Skill", "kill", "Address", "Current", "Job" };
    PageDisplayList list = DisplayListBuilder.Build(doc, 0);
    DumpKeywordsAndJobLine(list, keywords);
}

foreach (string keyword in dumpLineKeywords)
{
    Console.WriteLine();
    Console.WriteLine($"── Dump line: keyword=\"{keyword}\" (scanning all pages) ──");
    bool found = false;
    for (int pageIndex = 0; pageIndex < doc.PageCount; pageIndex++)
    {
        PageDisplayList list = DisplayListBuilder.Build(doc, pageIndex);
        if (TryDumpKeywordLine(list, keyword, pageIndex + 1))
        {
            found = true;
            break;
        }
    }
    if (!found)
    {
        Console.WriteLine($"  (no line containing \"{keyword}\" found on any page)");
    }
}

foreach (int pageNumber in dumpStreamPages)
{
    Console.WriteLine();
    Console.WriteLine($"── Dump stream: page={pageNumber} ──");
    if (pageNumber < 1 || pageNumber > doc.PageCount)
    {
        Console.WriteLine($"  (page {pageNumber} out of range 1..{doc.PageCount})");
        continue;
    }
    DumpContentStream(doc, pageNumber - 1);
}

return 0;

// ── Helpers ──────────────────────────────────────────────────────────

static void PrintOutline(IReadOnlyList<OutlineItem> items, int depth)
{
    string indent = new string(' ', depth * 2);
    foreach (OutlineItem item in items)
    {
        string pageRef = item.DestinationPageIndex >= 0
            ? $"  → page {item.DestinationPageIndex + 1}"
            : string.Empty;
        Console.WriteLine($"{indent}- {item.Title}{pageRef}");
        if (item.Children.Count > 0) { PrintOutline(item.Children, depth + 1); }
    }
}

static string Escape(string s)
{
    StringBuilder sb = new();
    foreach (char c in s)
    {
        if (c == '"') { sb.Append("\\\""); }
        else if (c == '\\') { sb.Append("\\\\"); }
        else if (c < 0x20) { sb.Append($"\\x{(int)c:X2}"); }
        else { sb.Append(c); }
    }
    return sb.ToString();
}

static string TextOf(TextOp op)
{
    StringBuilder sb = new();
    foreach (DisplayListGlyph g in op.Glyphs) { sb.Append(g.Unicode); }
    return sb.ToString();
}

static List<string> ParseFlagArgs(string[] args, string flag)
{
    List<string> values = new();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == flag && i + 1 < args.Length)
        {
            values.Add(args[i + 1]);
        }
    }
    return values;
}

static string[] StripFlagValues(string[] args)
{
    HashSet<string> flagsWithArg = new() { "--dump-line", "--dump-stream" };
    List<string> positional = new();
    for (int i = 0; i < args.Length; i++)
    {
        string a = args[i];
        if (flagsWithArg.Contains(a) && i + 1 < args.Length)
        {
            i++;
            continue;
        }
        if (a.StartsWith("--")) { continue; }
        positional.Add(a);
    }
    return positional.ToArray();
}

// ── Content stream dump ──────────────────────────────────────────────

static void DumpContentStream(PdfDocument doc, int pageIndex)
{
    PdfPage page = doc.Pages[pageIndex];
    PdfPrimitive? contentsRef = page.Contents;
    if (contentsRef is null)
    {
        Console.WriteLine("  (page has no Contents entry)");
        return;
    }

    // Resolve & concatenate stream bytes. The Contents entry can be a single
    // stream, a single stream reference, an array of streams, or an array of
    // stream references. PDF 32000-1 §7.8.2.
    byte[] bytes = LoadAndDecodeContents(doc, contentsRef);
    if (bytes.Length == 0)
    {
        Console.WriteLine("  (decoded content stream is empty)");
        return;
    }

    Console.WriteLine($"  Page {pageIndex + 1} content stream: {bytes.Length:N0} decoded bytes");
    Console.WriteLine();

    // Walk the tokens, accumulating operands until we hit a Keyword (operator).
    // Inline arrays accumulate as a single "[ ... ]" pseudo-operand. We don't
    // try to recurse into dictionaries inside content streams — they appear
    // only in inline-image operators which we render as one opaque block.
    List<string> operands = new();
    using MemoryStream ms = new(bytes);
    using PdfTokenizer tokenizer = new(ms, leaveOpen: false);

    int opCounter = 0;

    while (true)
    {
        PdfToken token = tokenizer.Read();
        if (token.IsEndOfStream) { break; }

        switch (token.Type)
        {
            case PdfTokenType.Integer:
            case PdfTokenType.Real:
            case PdfTokenType.Name:
            case PdfTokenType.LiteralString:
            case PdfTokenType.HexString:
            case PdfTokenType.True:
            case PdfTokenType.False:
            case PdfTokenType.Null:
                operands.Add(FormatToken(token));
                break;

            case PdfTokenType.ArrayStart:
                operands.Add(ReadInlineArray(tokenizer));
                break;

            case PdfTokenType.Keyword:
                string op = TokenRawText(token);
                string operandsText = operands.Count > 0
                    ? string.Join(" ", operands) + " "
                    : string.Empty;
                Console.WriteLine($"  [op {opCounter,5}] {operandsText}{op}");
                operands.Clear();
                opCounter++;
                break;

            // BI/ID/EI inline image: ID consumes raw bytes until EI. We don't
            // special-case these; if the test corpus uses them, the dump
            // would become noisy. Not the case for Word PDFs.
            case PdfTokenType.DictionaryStart:
            case PdfTokenType.DictionaryEnd:
                operands.Add(token.Type.ToString());
                break;

            default:
                operands.Add($"<{token.Type}:{TokenRawText(token)}>");
                break;
        }
    }

    if (operands.Count > 0)
    {
        Console.WriteLine($"  [op {opCounter,5}] {string.Join(" ", operands)} (trailing operands, no operator)");
    }
}

static string ReadInlineArray(PdfTokenizer tokenizer)
{
    StringBuilder sb = new();
    sb.Append('[');
    bool first = true;
    while (true)
    {
        PdfToken token = tokenizer.Read();
        if (token.IsEndOfStream) { sb.Append(" <eof>"); break; }
        if (token.Type == PdfTokenType.ArrayEnd) { break; }
        if (!first) { sb.Append(' '); }
        first = false;
        sb.Append(FormatToken(token));
    }
    sb.Append(']');
    return sb.ToString();
}

static string FormatToken(PdfToken token) => token.Type switch
{
    PdfTokenType.Name => "/" + TokenRawText(token),
    PdfTokenType.LiteralString => "(" + EscapeStringContent(TokenRawText(token)) + ")",
    PdfTokenType.HexString => "<" + TokenRawText(token) + ">",
    _ => TokenRawText(token),
};

static string TokenRawText(PdfToken token)
{
    if (token.RawBytes is null || token.RawBytes.Length == 0) { return string.Empty; }
    // Bytes are Latin-1 in content streams (PDF 32000-1 §7.2.2). We don't
    // attempt CMap decoding here — that's the whole point of this dump:
    // show what's literally in the stream, not the interpreted text.
    return Encoding.Latin1.GetString(token.RawBytes);
}

static string EscapeStringContent(string s)
{
    StringBuilder sb = new(s.Length);
    foreach (char c in s)
    {
        if (c == '\\') { sb.Append("\\\\"); }
        else if (c == '(') { sb.Append("\\("); }
        else if (c == ')') { sb.Append("\\)"); }
        else if (c < 0x20 || c == 0x7F) { sb.Append($"\\x{(int)c:X2}"); }
        else { sb.Append(c); }
    }
    return sb.ToString();
}

static byte[] LoadAndDecodeContents(PdfDocument doc, PdfPrimitive contentsRef)
{
    FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
    PdfPrimitive resolved = doc.Objects.Resolve(contentsRef);
    using MemoryStream merged = new();

    if (resolved is PdfStream single)
    {
        byte[] data = DecodeOne(pipeline, single);
        merged.Write(data, 0, data.Length);
    }
    else if (resolved is PdfArray arr)
    {
        for (int i = 0; i < arr.Count; i++)
        {
            PdfPrimitive entry = arr[i];
            if (doc.Objects.Resolve(entry) is PdfStream s)
            {
                if (i > 0) { merged.WriteByte((byte)' '); }
                byte[] data = DecodeOne(pipeline, s);
                merged.Write(data, 0, data.Length);
            }
        }
    }
    return merged.ToArray();
}

static byte[] DecodeOne(FilterPipeline pipeline, PdfStream stream)
{
    if (!stream.IsFiltered) { return stream.RawBytes; }
    PdfPrimitive? filter = stream.Filter;
    if (filter is PdfName fn)
    {
        string resolved = FilterRegistry.ResolveAlias(fn.Value);
        return pipeline.Decode(resolved, stream.RawBytes, null);
    }
    if (filter is PdfArray fa)
    {
        byte[] data = stream.RawBytes;
        for (int i = 0; i < fa.Count; i++)
        {
            if (fa[i] is PdfName fai)
            {
                string resolved = FilterRegistry.ResolveAlias(fai.Value);
                data = pipeline.Decode(resolved, data, null);
            }
        }
        return data;
    }
    return stream.RawBytes;
}

// ── Display-list line dump ───────────────────────────────────────────

static bool TryDumpKeywordLine(PageDisplayList list, string keyword, int displayPageNumber)
{
    Dictionary<double, List<(int OpIdx, TextOp Op)>> linesByY = new();
    int scanIdx = 0;
    foreach (RenderOp probe in list)
    {
        if (probe is TextOp t)
        {
            double yBucket = Math.Round(t.Transform.F * 2) / 2;
            if (!linesByY.TryGetValue(yBucket, out var bucket))
            {
                bucket = new List<(int, TextOp)>();
                linesByY[yBucket] = bucket;
            }
            bucket.Add((scanIdx, t));
        }
        scanIdx++;
    }

    foreach ((double y, List<(int OpIdx, TextOp Op)> ops) in linesByY)
    {
        var sorted = ops.OrderBy(pair => pair.Op.Transform.E).ToList();
        StringBuilder lineSb = new();
        foreach ((_, TextOp t) in sorted) { lineSb.Append(TextOf(t)); }
        string lineText = lineSb.ToString();
        if (!lineText.Contains(keyword, StringComparison.Ordinal)) { continue; }

        int firstLineOpIdx = ops.Min(p => p.OpIdx);
        int lastLineOpIdx = ops.Max(p => p.OpIdx);

        Console.WriteLine($"  Found on page {displayPageNumber}, Y={y:F2}");
        Console.WriteLine($"  Reconstructed line text: \"{Escape(lineText)}\"");
        Console.WriteLine();

        int opIdx = 0;
        int textIdx = 0;
        int matched = 0;
        int nonTextOpsInWindow = 0;
        foreach (RenderOp op in list)
        {
            if (op is TextOp tOp)
            {
                double yBucket = Math.Round(tOp.Transform.F * 2) / 2;
                if (Math.Abs(yBucket - y) < 0.01)
                {
                    matched++;
                    Console.WriteLine($"[op={opIdx,5} t={textIdx,4}] TextOp        x={tOp.Transform.E,8:F2} y={tOp.Transform.F,8:F2} fs={tOp.FontSize,5:F1} font={tOp.BaseFont,-30} glyphs={tOp.Glyphs.Count,3} text=\"{Escape(TextOf(tOp))}\"");
                    Console.Write("                       positions: ");
                    for (int i = 0; i < tOp.Glyphs.Count && i < 25; i++)
                    {
                        DisplayListGlyph g = tOp.Glyphs[i];
                        Console.Write($"'{Escape(g.Unicode)}'@{g.X:F1}(+{g.Advance:F1}) ");
                    }
                    if (tOp.Glyphs.Count > 25) { Console.Write("..."); }
                    Console.WriteLine();
                }
                textIdx++;
            }
            else if (opIdx >= firstLineOpIdx && opIdx <= lastLineOpIdx)
            {
                Console.WriteLine($"[op={opIdx,5}        ] {LabelFor(op)}");
                nonTextOpsInWindow++;
            }
            opIdx++;
        }
        Console.WriteLine();
        Console.WriteLine($"  TextOps on line: {matched}; non-text ops in line window: {nonTextOpsInWindow}");
        Console.WriteLine($"  Line window: op indices [{firstLineOpIdx}..{lastLineOpIdx}]");
        return true;
    }
    return false;
}

static string LabelFor(RenderOp op)
{
    if (op is TransformOp xf) { return xf.Push ? "TransformOp Push" : "TransformOp Pop"; }
    if (op is OpacityOp oo)
    {
        return oo.Push
            ? $"OpacityOp   Push alpha={oo.Alpha:F2} isolated={oo.Isolated}"
            : "OpacityOp   Pop";
    }
    if (op is BlendModeOp bm)
    {
        return bm.Push
            ? $"BlendModeOp Push mode={bm.Mode}"
            : "BlendModeOp Pop";
    }
    if (op is ClipOp) { return "ClipOp"; }
    if (op is PathOp pp) { return $"PathOp        mode={pp.Mode}"; }
    if (op is ImageOp) { return "ImageOp"; }
    return op.GetType().Name;
}

static void DumpKeywordsAndJobLine(PageDisplayList list, string[] keywords)
{
    double? jobY = null;
    int firstJobLineOpIdx = -1;
    int lastJobLineOpIdx = -1;
    int scanIdx = 0;
    foreach (RenderOp probe in list)
    {
        if (probe is TextOp pt)
        {
            string ptText = TextOf(pt);
            if (jobY is null && ptText.Contains("Job", StringComparison.Ordinal))
            {
                jobY = pt.Transform.F;
            }
            if (jobY is not null && Math.Abs(pt.Transform.F - jobY.Value) < 0.5)
            {
                if (firstJobLineOpIdx < 0) { firstJobLineOpIdx = scanIdx; }
                lastJobLineOpIdx = scanIdx;
            }
        }
        scanIdx++;
    }

    int opIdx = 0;
    int textIdx = 0;
    int matched = 0;
    foreach (RenderOp op in list)
    {
        if (op is TextOp t)
        {
            string text = TextOf(t);
            bool hit = false;
            foreach (string k in keywords)
            {
                if (text.Contains(k, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
            }
            if (!hit && jobY is not null && Math.Abs(t.Transform.F - jobY.Value) < 0.5) { hit = true; }
            if (hit)
            {
                matched++;
                Console.WriteLine($"[op={opIdx,5} t={textIdx,4}] TextOp        x={t.Transform.E,8:F2} y={t.Transform.F,8:F2} fs={t.FontSize,5:F1} font={t.BaseFont,-30} glyphs={t.Glyphs.Count,3} text=\"{Escape(text)}\"");
            }
            textIdx++;
        }
        else if (firstJobLineOpIdx >= 0 && opIdx >= firstJobLineOpIdx && opIdx <= lastJobLineOpIdx)
        {
            Console.WriteLine($"[op={opIdx,5}        ] {LabelFor(op)}");
        }
        opIdx++;
    }
    Console.WriteLine();
    Console.WriteLine($"Total ops: {opIdx}; TextOps: {textIdx}; matched: {matched}");
    if (firstJobLineOpIdx >= 0)
    {
        Console.WriteLine($"Job-line window: op indices [{firstJobLineOpIdx}..{lastJobLineOpIdx}] (Y={jobY:F2})");
    }
}
