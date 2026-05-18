// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: read annotations from a PDF, then add a sticky-note and a
// "CONFIDENTIAL" stamp on page 1 and write a new PDF.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Annotations;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Annotations <input.pdf> <output.pdf>");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];

using FileStream input = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: true);

// 1. Read existing annotations
IReadOnlyList<PdfAnnotation> existing = AnnotationReader.GetAllAnnotations(document);
Console.WriteLine($"Found {existing.Count} existing annotation(s):");
foreach (PdfAnnotation a in existing)
{
    Console.WriteLine($"  page {a.PageIndex + 1}: {a.Type}  rect={a.Rect}");
    if (!string.IsNullOrEmpty(a.Contents))
    {
        Console.WriteLine($"    contents: {a.Contents}");
    }
}

// 2. Add a sticky-note plus a CONFIDENTIAL stamp on page 1
List<PdfAnnotation> additions = new()
{
    new TextAnnotation(
        pageIndex: 0,
        rect: new RectangleF(50, 700, 24, 24),
        contents: "Reviewed by clinical lead 2026-05-11",
        iconName: "Note",
        isOpen: false,
        color: ColorF.FromRgb(1f, 0.9f, 0f),
        author: "Dr Smith"),

    new StampAnnotation(
        pageIndex: 0,
        rect: new RectangleF(400, 700, 150, 40),
        stampName: "Confidential"),
};

using FileStream output = File.Create(outputPath);
AnnotationWriter.Add(output, document, additions);

Console.WriteLine();
Console.WriteLine($"Wrote {outputPath} with {additions.Count} new annotation(s).");
return 0;
