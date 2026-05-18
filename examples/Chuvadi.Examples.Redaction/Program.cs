// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: redact a rectangle on page 1 and verify the secret string is
// byte-level absent from the output (BASELINE B15).

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Redaction;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: Chuvadi.Examples.Redaction <input.pdf> <output.pdf> <secret-string-to-verify>");
    Console.Error.WriteLine(
        "Example: Chuvadi.Examples.Redaction patient.pdf patient-redacted.pdf 123-45-6789");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];
string secret = args[2];

using FileStream input = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

// Redact a rectangle on page 1 in PDF user space (points).
// Adjust the coordinates to match your document's PHI location.
RedactionOptions opts = new();
opts.Rectangles.Add(new RedactionRect(
    pageIndex: 0,
    bounds: new RectangleF(72, 700, 200, 20)));

using FileStream output = File.Create(outputPath);
Redactor.Apply(output, document, opts);

Console.WriteLine($"Wrote {outputPath}");

// Verify byte-level absence
byte[] outputBytes = File.ReadAllBytes(outputPath);
byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
bool found = IndexOf(outputBytes, secretBytes) >= 0;

if (found)
{
    Console.Error.WriteLine($"FAIL: '{secret}' still appears in {outputPath}.");
    Console.Error.WriteLine("The redaction rectangle did not cover the target.");
    return 3;
}

Console.WriteLine($"VERIFIED: '{secret}' is byte-level absent from the output.");
return 0;

static int IndexOf(byte[] haystack, byte[] needle)
{
    for (int i = 0; i + needle.Length <= haystack.Length; i++)
    {
        bool match = true;
        for (int j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j])
            {
                match = false;
                break;
            }
        }
        if (match)
        {
            return i;
        }
    }
    return -1;
}
