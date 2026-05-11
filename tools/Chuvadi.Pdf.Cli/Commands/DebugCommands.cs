// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Cli
// Low-level introspection commands for debugging malformed PDFs.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Cli.Commands;

// ── tokenize ──────────────────────────────────────────────────────────────

/// <summary>Dumps raw PDF tokens from a content stream.</summary>
public sealed class TokenizeCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi tokenize <input.pdf> [--page N]");
            return 2;
        }

        string inputPath = p.Positional[0];
        int pageIndex = int.Parse(p.Get("page", "0")!,
            System.Globalization.CultureInfo.InvariantCulture);

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
            {
                stderr.WriteLine($"tokenize: page {pageIndex} out of range");
                return 1;
            }

            byte[] content = LoadPageContent(doc, pageIndex);

            using (MemoryStream ms = new MemoryStream(content))
            using (PdfTokenizer tok = new PdfTokenizer(ms))
            {
                int idx = 0;

                while (true)
                {
                    PdfToken t = tok.Read();

                    if (t.IsEndOfStream)
                    {
                        break;
                    }

                    stdout.WriteLine($"{idx,5}  {t.Type,-14}  {t.RawText}");
                    idx++;
                }
            }
        }

        return 0;
    }

    private static byte[] LoadPageContent(PdfDocument doc, int pageIndex)
    {
        PdfPage page = doc.Pages[pageIndex];
        PdfPrimitive? contents = page.Contents;

        if (contents is null || contents is PdfNull)
        {
            return [];
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        PdfPrimitive resolved = doc.Objects.Resolve(contents);

        if (resolved is PdfStream stream)
        {
            return DecodeStream(stream, pipeline);
        }

        if (resolved is PdfArray arr)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    PdfPrimitive item = doc.Objects.Resolve(arr[i]);

                    if (item is PdfStream s)
                    {
                        byte[] decoded = DecodeStream(s, pipeline);
                        ms.Write(decoded, 0, decoded.Length);
                        ms.WriteByte(32);
                    }
                }

                return ms.ToArray();
            }
        }

        return [];
    }

    internal static byte[] DecodeStream(PdfStream stream, FilterPipeline pipeline)
    {
        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName fn)
        {
            return pipeline.Decode(FilterRegistry.ResolveAlias(fn.Value), stream.RawBytes, null);
        }

        if (filter is PdfArray fa)
        {
            byte[] data = stream.RawBytes;

            for (int i = 0; i < fa.Count; i++)
            {
                PdfName? n = fa.GetAs<PdfName>(i);

                if (n is not null)
                {
                    data = pipeline.Decode(FilterRegistry.ResolveAlias(n.Value), data, null);
                }
            }

            return data;
        }

        return stream.RawBytes;
    }
}

// ── dump-objects ──────────────────────────────────────────────────────────

/// <summary>Lists every indirect object with its ID and type.</summary>
public sealed class DumpObjectsCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi dump-objects <input.pdf>");
            return 2;
        }

        string inputPath = p.Positional[0];

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            // Force-load by walking pages so the store contains the full graph
            for (int i = 0; i < doc.PageCount; i++)
            {
                _ = doc.Pages[i].Dictionary;
            }

            stdout.WriteLine($"{"ID",-10}  {"Type",-18}  Summary");
            stdout.WriteLine(new string('-', 60));

            foreach (PdfIndirectObject obj in doc.Objects.Objects)
            {
                string idStr = $"{obj.Id.ObjectNumber} {obj.Id.Generation}";
                string typeName = obj.Value.GetType().Name;
                string summary = SummarizeValue(obj.Value);
                stdout.WriteLine($"{idStr,-10}  {typeName,-18}  {summary}");
            }
        }

        return 0;
    }

    private static string SummarizeValue(PdfPrimitive value)
    {
        if (value is PdfDictionary dict)
        {
            if (dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) && t is PdfName tn)
            {
                return $"<<{dict.Count} entries, /Type /{tn.Value}>>";
            }

            return $"<<{dict.Count} entries>>";
        }

        if (value is PdfStream stream)
        {
            return $"stream ({stream.RawBytes.Length} bytes)";
        }

        if (value is PdfArray arr)
        {
            return $"[{arr.Count} items]";
        }

        return value.ToString() ?? string.Empty;
    }
}

// ── parse-content ─────────────────────────────────────────────────────────

/// <summary>
/// Shows content stream operators with their operands. Walks tokens and
/// groups everything between keywords as a single operation.
/// </summary>
public sealed class ParseContentCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi parse-content <input.pdf> [--page N]");
            return 2;
        }

        string inputPath = p.Positional[0];
        int pageIndex = int.Parse(p.Get("page", "0")!,
            System.Globalization.CultureInfo.InvariantCulture);

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
            {
                stderr.WriteLine($"parse-content: page {pageIndex} out of range");
                return 1;
            }

            byte[] content = LoadContent(doc, pageIndex);
            WalkOperators(content, stdout);
        }

        return 0;
    }

    private static byte[] LoadContent(PdfDocument doc, int pageIndex)
    {
        PdfPage page = doc.Pages[pageIndex];
        PdfPrimitive? contents = page.Contents;

        if (contents is null || contents is PdfNull)
        {
            return [];
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        PdfPrimitive resolved = doc.Objects.Resolve(contents);

        if (resolved is PdfStream stream)
        {
            return TokenizeCommand.DecodeStream(stream, pipeline);
        }

        if (resolved is PdfArray arr)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    PdfPrimitive item = doc.Objects.Resolve(arr[i]);

                    if (item is PdfStream s)
                    {
                        byte[] decoded = TokenizeCommand.DecodeStream(s, pipeline);
                        ms.Write(decoded, 0, decoded.Length);
                        ms.WriteByte(32);
                    }
                }

                return ms.ToArray();
            }
        }

        return [];
    }

    private static void WalkOperators(byte[] content, TextWriter stdout)
    {
        using (MemoryStream ms = new MemoryStream(content))
        using (PdfTokenizer tok = new PdfTokenizer(ms))
        {
            System.Collections.Generic.List<string> operands =
                new System.Collections.Generic.List<string>();

            while (true)
            {
                PdfToken t = tok.Read();

                if (t.IsEndOfStream)
                {
                    break;
                }

                if (t.Type == PdfTokenType.Keyword)
                {
                    stdout.WriteLine($"{t.RawText,-6}  [{string.Join(" ", operands)}]");
                    operands.Clear();
                }
                else
                {
                    operands.Add(t.RawText);
                }
            }
        }
    }
}

// ── decode-stream ─────────────────────────────────────────────────────────

/// <summary>Decodes a raw binary file through a named filter.</summary>
public sealed class DecodeStreamCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi decode-stream <input.bin> --filter FlateDecode [--output decoded.bin]");
            return 2;
        }

        string inputPath = p.Positional[0];
        string filterName = p.Get("filter") ?? throw new ArgumentException("--filter is required");
        string? outputPath = p.Get("output");

        byte[] input = File.ReadAllBytes(inputPath);
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] decoded = pipeline.Decode(FilterRegistry.ResolveAlias(filterName), input, null);

        if (outputPath is null)
        {
            stdout.WriteLine($"decode-stream: {input.Length} bytes → {decoded.Length} bytes (no --output, dropped)");
        }
        else
        {
            File.WriteAllBytes(outputPath, decoded);
            stdout.WriteLine($"decode-stream: wrote {outputPath} ({decoded.Length} bytes)");
        }

        return 0;
    }
}

// ── inspect-xref ──────────────────────────────────────────────────────────

/// <summary>Prints the cross-reference table entries.</summary>
public sealed class InspectXrefCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi inspect-xref <input.pdf>");
            return 2;
        }

        string inputPath = p.Positional[0];

        using (FileStream fs = File.OpenRead(inputPath))
        {
            XrefTable xref = XrefTable.Parse(fs);

            stdout.WriteLine($"{"Obj",-6}  {"Gen",-5}  {"Type",-8}  Offset");
            stdout.WriteLine(new string('-', 40));

            foreach (XrefEntry entry in xref.Entries)
            {
                string offset = entry.IsInUse
                    ? entry.ByteOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "—";
                stdout.WriteLine($"{entry.ObjectNumber,-6}  {entry.Generation,-5}  {entry.Type,-8}  {offset}");
            }
        }

        return 0;
    }
}

// ── validate-fonts ────────────────────────────────────────────────────────

/// <summary>Lists embedded fonts and their basic properties.</summary>
public sealed class ValidateFontsCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi validate-fonts <input.pdf>");
            return 2;
        }

        string inputPath = p.Positional[0];

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            // Force-load page graph
            for (int i = 0; i < doc.PageCount; i++)
            {
                _ = doc.Pages[i].Dictionary;
            }

            int fontsFound = 0;

            foreach (PdfIndirectObject obj in doc.Objects.Objects)
            {
                if (obj.Value is not PdfDictionary dict)
                {
                    continue;
                }

                if (!dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) ||
                    t is not PdfName tn || tn.Value != "Font")
                {
                    continue;
                }

                fontsFound++;
                string subtype = dict.GetName(PdfName.Intern("Subtype"))?.Value ?? "—";
                string baseFont = dict.GetName(PdfName.Intern("BaseFont"))?.Value ?? "—";
                string encoding = dict.GetName(PdfName.Intern("Encoding"))?.Value ?? "—";
                bool hasDescriptor = dict.TryGetValue(PdfName.Intern("FontDescriptor"), out _);

                stdout.WriteLine($"Font #{obj.Id.ObjectNumber}:");
                stdout.WriteLine($"  Subtype:    {subtype}");
                stdout.WriteLine($"  BaseFont:   {baseFont}");
                stdout.WriteLine($"  Encoding:   {encoding}");
                stdout.WriteLine($"  Descriptor: {(hasDescriptor ? "yes" : "no")}");
            }

            if (fontsFound == 0)
            {
                stdout.WriteLine("(no fonts found)");
            }
        }

        return 0;
    }
}
