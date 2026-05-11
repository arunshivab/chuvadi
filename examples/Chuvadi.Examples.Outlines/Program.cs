// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: print the document outline (bookmark) tree.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Outlines <input.pdf>");
    return 1;
}

using FileStream input = File.OpenRead(args[0]);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

IReadOnlyList<OutlineItem> outline = OutlineReader.GetOutlines(document);

if (outline.Count == 0)
{
    Console.WriteLine("Document has no outline (bookmarks).");
    return 0;
}

PrintOutline(outline, 0);
return 0;

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
