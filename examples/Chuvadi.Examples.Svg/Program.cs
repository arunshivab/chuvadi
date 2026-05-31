// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export example

using System;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Svg;

// Author a sample PDF with the Authoring module, then export each page to SVG.

var builder = PdfDocumentBuilder.Create()
    .SetTitle("SVG export demo")
    .SetHeader((page, num, total) => page.DrawText(
        $"Demo Report — Page {num} of {total}",
        50, 20, StandardFonts.HelveticaBold, 10, Colors.DarkGray));

var p1 = builder.AddPage(PageSize.A4);
p1.DrawText("Patient Vital Trends",
    50, 60, StandardFonts.HelveticaBold, 20, Colors.Black);
p1.DrawText("Q2 2026 summary",
    50, 95, StandardFonts.HelveticaOblique, 11, Colors.Gray);
p1.DrawTable(50, 130, 495)
    .AddColumn("Metric", 0.4)
    .AddColumn("Min", 0.15)
    .AddColumn("Max", 0.15)
    .AddColumn("Mean", 0.15)
    .AddColumn("Status", 0.15)
    .HeaderStyle(bold: true, background: Color.FromHex("#e8eef5"))
    .Border(BorderStyle.Single, Colors.Gray)
    .AddRow("Heart Rate", "62", "98", "78", "Normal")
    .AddRow("Sys BP", "108", "138", "122", "Normal")
    .AddRow("Dia BP", "68", "88", "76", "Normal")
    .AddRow("Temperature", "36.4", "37.2", "36.7", "Normal")
    .AddRow("SpO2", "96", "99", "98", "Normal")
    .Render();

var p2 = builder.AddPage(PageSize.A4);
p2.DrawText("Notes", 50, 60, StandardFonts.HelveticaBold, 16, Colors.Black);
p2.DrawTextBlock(
    "Vitals over Q2 show stable post-discharge trends. No interventions required. "
    + "Patient adherent to medications. Follow-up in 6 weeks.",
    50, 100, 495, 80,
    StandardFonts.Helvetica, 11, Colors.Black);

byte[] pdfBytes = builder.ToByteArray();
string outDir = AppContext.BaseDirectory;

// Save PDF
string pdfPath = Path.Combine(outDir, "demo.pdf");
File.WriteAllBytes(pdfPath, pdfBytes);
Console.WriteLine($"PDF: {pdfPath} ({pdfBytes.Length} bytes)");

// Export each page to its own SVG
using var doc = PdfDocument.Open(new MemoryStream(pdfBytes), leaveOpen: false);
int n = 0;
var renderer = new SvgRenderer();
foreach (string svg in renderer.RenderPages(doc))
{
    string svgPath = Path.Combine(outDir, $"demo-page{n + 1}.svg");
    File.WriteAllText(svgPath, svg);
    Console.WriteLine($"SVG{n + 1}: {svgPath} ({svg.Length} chars)");
    n++;
}
