// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: stamp a diagonal text watermark across every page.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Watermark;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Watermark <input.pdf> <output.pdf> <text>");
    Console.Error.WriteLine("Example: Chuvadi.Examples.Watermark report.pdf report-draft.pdf DRAFT");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];
string text = args[2];

using FileStream input = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

TextWatermarkOptions opts = new(text)
{
    FontSize = 72.0,
    Color = ColorF.FromGray(0.5f),
    Opacity = 0.25f,
    RotationDegrees = 45.0,
};

using FileStream output = File.Create(outputPath);
WatermarkStamper.ApplyText(output, document, opts);

Console.WriteLine($"Wrote {outputPath}");
Console.WriteLine($"Stamped \"{text}\" across {document.PageCount} pages.");
return 0;
