// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: extract text from a PDF using both strategies.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.TextExtraction <input.pdf>");
    return 1;
}

string inputPath = args[0];

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 2;
}

using FileStream fs = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(fs, leaveOpen: false);

Console.WriteLine($"Document: {inputPath}");
Console.WriteLine($"Pages: {document.PageCount}");
Console.WriteLine();

// Strategy 1: operator-order extraction (fastest, preserves PDF operator sequence)
Console.WriteLine("── Operator strategy ──────────────────────────────────────");
TextExtractor opExtractor = new(document.Objects, ExtractionStrategy.Operator);
for (int i = 0; i < document.PageCount; i++)
{
    string text = opExtractor.ExtractText(document.Pages[i]);
    Console.WriteLine($"[Page {i + 1}]");
    Console.WriteLine(text);
    Console.WriteLine();
}

// Strategy 2: layout-aware (handles multi-column, tables)
Console.WriteLine("── Layout strategy ────────────────────────────────────────");
TextExtractor layoutExtractor = new(document.Objects, ExtractionStrategy.Layout);
for (int i = 0; i < document.PageCount; i++)
{
    string text = layoutExtractor.ExtractText(document.Pages[i]);
    Console.WriteLine($"[Page {i + 1}]");
    Console.WriteLine(text);
    Console.WriteLine();
}

return 0;
