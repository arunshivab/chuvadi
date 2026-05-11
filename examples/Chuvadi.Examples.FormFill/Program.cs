// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: enumerate AcroForm fields in a PDF, then fill them and write a
// new PDF.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Chuvadi.Examples.FormFill <input.pdf>");
    Console.Error.WriteLine("  Chuvadi.Examples.FormFill <input.pdf> <output.pdf> field1=value1 field2=value2 ...");
    return 1;
}

string inputPath = args[0];

using FileStream input = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

IReadOnlyList<FormField> fields = FormReader.GetFields(document);

if (fields.Count == 0)
{
    Console.WriteLine("No AcroForm fields found.");
    return 0;
}

Console.WriteLine($"Found {fields.Count} field(s):");
foreach (FormField f in fields)
{
    Console.WriteLine($"  {f.Type,-10}  {f.FullyQualifiedName,-30}  current: \"{f.Value}\"");
}

// One-arg mode: just list. Three-or-more-arg mode: fill and write.
if (args.Length < 3)
{
    return 0;
}

string outputPath = args[1];
Dictionary<string, string> values = new();

for (int i = 2; i < args.Length; i++)
{
    int eq = args[i].IndexOf('=');
    if (eq <= 0) { continue; }
    values[args[i][..eq]] = args[i][(eq + 1)..];
}

using FileStream output = File.Create(outputPath);
FormFiller.Fill(output, document, values);

Console.WriteLine();
Console.WriteLine($"Wrote {outputPath} with {values.Count} value(s) filled.");
return 0;
