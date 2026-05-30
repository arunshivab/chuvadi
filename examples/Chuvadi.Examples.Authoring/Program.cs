// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.3 — Authoring module example

using System;
using System.IO;
using Chuvadi.Pdf.Authoring;

// LiPi-style discharge summary: header + body prose + patient table + footer.

var doc = PdfDocumentBuilder.Create()
    .SetTitle("Patient Discharge Summary")
    .SetAuthor("LiPi HIS")
    .SetSubject("Discharge")
    .SetHeader((page, num, total) =>
    {
        page.DrawText("LiPi HIS — Patient Discharge Summary",
            x: 50, y: 20,
            font: StandardFonts.HelveticaBold, size: 10, color: Colors.DarkGray);
        page.DrawText($"Page {num} of {total}",
            x: page.Width - 100, y: 20,
            font: StandardFonts.Helvetica, size: 10, color: Colors.Gray);
        page.DrawLine(50, 38, page.Width - 50, 38, Colors.LightGray, 0.5);
    })
    .SetFooter((page, num, total) =>
    {
        page.DrawText("CONFIDENTIAL — for clinical use only",
            x: 50, y: page.Height - 25,
            font: StandardFonts.HelveticaOblique, size: 8, color: Colors.Gray);
    });

var p = doc.AddPage(PageSize.A4);

// Title block
p.DrawText("Discharge Summary",
    x: 50, y: 60,
    font: StandardFonts.HelveticaBold, size: 20, color: Colors.Black);
p.DrawText("Patient ID: MRN-2026-04217",
    x: 50, y: 95,
    font: StandardFonts.Helvetica, size: 11, color: Colors.DarkGray);

// Patient details table
p.DrawTable(x: 50, y: 130, width: 495)
    .AddColumn("Field", 0.3)
    .AddColumn("Value", 0.7)
    .Font(StandardFonts.Helvetica, 10)
    .HeaderStyle(bold: true, background: Color.FromHex("#e8eef5"))
    .Border(BorderStyle.Single, Colors.Gray, 0.5)
    .AddRow("Patient Name", "Sharma, Vikram")
    .AddRow("Date of Birth", "1975-08-14")
    .AddRow("Admission Date", "2026-05-12")
    .AddRow("Discharge Date", "2026-05-16")
    .AddRow("Attending", "Dr. Khaire (Cardiology)")
    .AddRow("Diagnosis", "Acute MI, post-PCI")
    .Render();

// Body prose
p.DrawTextBlock(
    "Patient presented to the emergency department with crushing substernal chest pain " +
    "of two hours' duration, radiating to the left arm. Initial ECG showed ST elevation " +
    "in leads II, III, and aVF consistent with inferior wall myocardial infarction. " +
    "Patient underwent emergency cardiac catheterization with successful primary PCI " +
    "and drug-eluting stent placement to the right coronary artery. Post-procedure course " +
    "was uneventful. Patient is discharged in stable condition on dual antiplatelet " +
    "therapy, statin, beta-blocker, and ACE inhibitor.",
    x: 50, y: 380, width: 495, height: 200,
    font: StandardFonts.Helvetica, size: 11, color: Colors.Black,
    align: TextAlignment.Justify,
    lineHeight: 1.4);

// Hyperlink to portal
p.DrawHyperlink(
    "View full medical record online",
    x: 50, y: 620,
    font: StandardFonts.Helvetica, size: 10, color: Colors.LinkBlue,
    uri: "https://portal.lipi.example/mrn/2026-04217");

string outDir = AppContext.BaseDirectory;
string outPath = Path.Combine(outDir, "discharge_summary.pdf");
doc.Save(File.Create(outPath));
Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length} bytes)");
