// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: exercise the high-level IPdfReader facade end-to-end.
// Open a document, print metadata, list outlines, search for a query,
// render the first page to SVG, and report text-run geometry.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Reader;
using Chuvadi.Pdf.Rendering.DisplayList;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Reader <input.pdf> [search-query] [password]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Exercises the IPdfReader facade against the given PDF:");
    Console.Error.WriteLine("  - opens the document (use third arg for encrypted PDFs)");
    Console.Error.WriteLine("  - prints metadata and encryption info");
    Console.Error.WriteLine("  - lists the outline (bookmark) tree");
    Console.Error.WriteLine("  - searches for the query (defaults to 'the')");
    Console.Error.WriteLine("  - renders page 1 to page-1.svg next to the input");
    Console.Error.WriteLine("  - prints text-run geometry on page 1");
    return 1;
}

string inputPath = args[0];
string query = args.Length > 1 ? args[1] : "the";
string? password = args.Length > 2 ? args[2] : null;

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 1;
}

IPdfReader reader = new ChuvadiPdfReader();

await using FileStream input = File.OpenRead(inputPath);
string fileName = Path.GetFileName(inputPath);

using PdfDocument doc = await reader.OpenAsync(input, fileName, password);

// ── File summary ──────────────────────────────────────────────────────────
Console.WriteLine($"File: {fileName}  ({doc.PageCount} page{(doc.PageCount == 1 ? string.Empty : "s")})");
Console.WriteLine();

// ── Encryption ────────────────────────────────────────────────────────────
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

// ── Metadata ──────────────────────────────────────────────────────────────
Console.WriteLine("Metadata:");
Console.WriteLine($"  Title:        {doc.Title ?? "(none)"}");
Console.WriteLine($"  Author:       {doc.Author ?? "(none)"}");
Console.WriteLine($"  Subject:      {doc.Subject ?? "(none)"}");
Console.WriteLine($"  Creator:      {doc.Creator ?? "(none)"}");
Console.WriteLine($"  Producer:     {doc.Producer ?? "(none)"}");
Console.WriteLine($"  CreationDate: {doc.CreationDate?.ToString("o") ?? "(none)"}");
Console.WriteLine($"  ModDate:      {doc.ModDate?.ToString("o") ?? "(none)"}");
Console.WriteLine();

// ── Outline ───────────────────────────────────────────────────────────────
IReadOnlyList<OutlineItem> outlines = await reader.GetOutlinesAsync(doc);
Console.WriteLine($"Outline: {outlines.Count} top-level entr{(outlines.Count == 1 ? "y" : "ies")}");
if (outlines.Count > 0)
{
    PrintOutline(outlines, depth: 1);
}
Console.WriteLine();

// ── Search ────────────────────────────────────────────────────────────────
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
if (matchCount == 0)
{
    Console.WriteLine("  (no matches)");
}
else if (matchCount > 5)
{
    Console.WriteLine($"  ... and {matchCount - 5} more (total: {matchCount})");
}
else
{
    Console.WriteLine($"  (total: {matchCount})");
}
Console.WriteLine();

// ── Render page 1 ─────────────────────────────────────────────────────────
string svg = await reader.RenderPageSvgAsync(doc, pageIndex: 0);
string svgPath = Path.Combine(Environment.CurrentDirectory, "page-1.svg");
await File.WriteAllTextAsync(svgPath, svg);
Console.WriteLine($"Render: page 1 → {svgPath} ({svg.Length:N0} chars)");

// ── Text runs on page 1 ───────────────────────────────────────────────────
IReadOnlyList<TextRun> runs = await reader.GetTextRunsAsync(doc, pageIndex: 0);
Console.WriteLine($"Text runs on page 1: {runs.Count}");

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────

static void PrintOutline(IReadOnlyList<OutlineItem> items, int depth)
{
    string indent = new string(' ', depth * 2);
    foreach (OutlineItem item in items)
    {
        string pageRef = item.DestinationPageIndex >= 0
            ? $"  → page {item.DestinationPageIndex + 1}"
            : string.Empty;
        Console.WriteLine($"{indent}- {item.Title}{pageRef}");
        if (item.Children.Count > 0)
        {
            PrintOutline(item.Children, depth + 1);
        }
    }
}
