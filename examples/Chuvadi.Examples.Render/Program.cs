// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: rasterize every page of a PDF to PNG at a given DPI.
// Uses Chuvadi's zero-dependency scanline rasterizer — no native libraries.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Render <input.pdf> <output-dir> [dpi]");
    Console.Error.WriteLine("Example: Chuvadi.Examples.Render report.pdf pages 150");
    return 1;
}

string inputPath = args[0];
string outputDir = args[1];
double dpi       = args.Length >= 3 ? double.Parse(args[2]) : 96.0;

Directory.CreateDirectory(outputDir);

using FileStream input = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

RenderOptions options = new()
{
    Dpi = dpi,
};

PageRasterizer rasterizer = new(document.Objects, options);

for (int i = 0; i < document.PageCount; i++)
{
    byte[] png = rasterizer.RasterizeToPng(document.Pages[i]);
    string outPath = Path.Combine(outputDir, $"page_{i + 1:D3}.png");
    File.WriteAllBytes(outPath, png);
    Console.WriteLine($"Wrote {outPath}");
}

return 0;
