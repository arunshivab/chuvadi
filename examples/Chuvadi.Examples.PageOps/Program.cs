// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: page-level operations — merge, split, delete, rotate.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

if (args.Length == 0)
{
    Usage();
    return 1;
}

string verb = args[0];

switch (verb)
{
    case "merge": return Merge(args);
    case "split": return Split(args);
    case "delete": return Delete(args);
    case "rotate": return Rotate(args);
    default: Usage(); return 1;
}

static void Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Chuvadi.Examples.PageOps merge  <output.pdf> <in1.pdf> <in2.pdf> [<in3.pdf>...]");
    Console.Error.WriteLine("  Chuvadi.Examples.PageOps split  <input.pdf> <output-dir>");
    Console.Error.WriteLine("  Chuvadi.Examples.PageOps delete <input.pdf> <output.pdf> <page1,page2,...>");
    Console.Error.WriteLine("  Chuvadi.Examples.PageOps rotate <input.pdf> <output.pdf> <page> <degrees>");
}

static int Merge(string[] args)
{
    if (args.Length < 4) { Usage(); return 1; }

    string outPath = args[1];
    List<PdfDocument> docs = new();
    List<FileStream> streams = new();

    try
    {
        for (int i = 2; i < args.Length; i++)
        {
            FileStream fs = File.OpenRead(args[i]);
            streams.Add(fs);
            docs.Add(PdfDocument.Open(fs, leaveOpen: true));
        }

        using FileStream output = File.Create(outPath);
        PageOperations.Merge(output, docs.ToArray());
        Console.WriteLine($"Merged {docs.Count} documents → {outPath}");
        return 0;
    }
    finally
    {
        foreach (PdfDocument d in docs) { d.Dispose(); }
        foreach (FileStream s in streams) { s.Dispose(); }
    }
}

static int Split(string[] args)
{
    if (args.Length < 3) { Usage(); return 1; }

    string inputPath = args[1];
    string outputDir = args[2];
    Directory.CreateDirectory(outputDir);

    using FileStream fs = File.OpenRead(inputPath);
    using PdfDocument document = PdfDocument.Open(fs, leaveOpen: false);

    List<MemoryStream> pages = PageOperations.SplitPages(document);

    for (int i = 0; i < pages.Count; i++)
    {
        string outPath = Path.Combine(outputDir, $"page_{i + 1:D3}.pdf");
        File.WriteAllBytes(outPath, pages[i].ToArray());
        Console.WriteLine($"Wrote {outPath}");
        pages[i].Dispose();
    }
    return 0;
}

static int Delete(string[] args)
{
    if (args.Length < 4) { Usage(); return 1; }

    string inputPath = args[1];
    string outputPath = args[2];
    int[] pages = ParsePages(args[3]);

    using FileStream input = File.OpenRead(inputPath);
    using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);
    using FileStream output = File.Create(outputPath);

    PageOperations.DeletePages(output, document, pages);
    Console.WriteLine($"Deleted pages [{string.Join(", ", pages)}] → {outputPath}");
    return 0;
}

static int Rotate(string[] args)
{
    if (args.Length < 5) { Usage(); return 1; }

    string inputPath = args[1];
    string outputPath = args[2];
    int pageIndex = int.Parse(args[3]);
    int degrees = int.Parse(args[4]);

    using FileStream input = File.OpenRead(inputPath);
    using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);
    using FileStream output = File.Create(outputPath);

    PageOperations.RotatePages(output, document, degrees, new[] { pageIndex });
    Console.WriteLine($"Rotated page {pageIndex} by {degrees}° → {outputPath}");
    return 0;
}

static int[] ParsePages(string csv)
{
    string[] parts = csv.Split(',');
    int[] result = new int[parts.Length];
    for (int i = 0; i < parts.Length; i++) { result[i] = int.Parse(parts[i]); }
    return result;
}
