using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.ManualTests;

public static class ReaderRoundTripTest
{
    public static void Run(string outDir)
    {
        var path = Path.Combine(outDir, "roundtrip.docx");
        var path2 = Path.Combine(outDir, "roundtrip-edited.docx");

        Program.Check("Write source document", () =>
        {
            var doc = new Document();
            doc.AddParagraph("Report Title", ParagraphStyle.Title);
            doc.AddHeading("Section A", 1);
            doc.AddParagraph()
                .Text("Plain then ")
                .Text("bold red", new TextFormat { Bold = true, ColorHex = "FF0000" })
                .Text(" then plain.");
            doc.AddBullet("Bullet item");
            doc.AddNumbered("Numbered item");
            var t = doc.AddTable(2);
            t.HeaderRow("K", "V");
            t.AddRow("Alpha", "1");
            doc.SaveTo(path);
        });

        Program.Check("DocxReader extracts paragraphs with styles", () =>
        {
            using var r = DocxReader.Open(path);
            var paras = r.Paragraphs().ToList();
            Program.AssertTrue(paras.Any(p => p.Text == "Report Title" && p.StyleId == "Title"), "Title paragraph");
            Program.AssertTrue(paras.Any(p => p.Text == "Section A" && p.StyleId == "Heading1"), "Heading paragraph");
            Program.AssertTrue(paras.Any(p => p.Text.Contains("bold red")), "mixed-format paragraph text");
            // Table cell paragraphs must NOT leak into Paragraphs().
            Program.AssertTrue(!paras.Any(p => p.Text == "Alpha"), "table cell leaked into Paragraphs()");
        });

        Program.Check("DocxReader.ExtractText covers body and tables", () =>
        {
            using var r = DocxReader.Open(path);
            var text = r.ExtractText();
            Program.AssertContains(text, "Report Title", "body text");
            Program.AssertContains(text, "Alpha", "table text");
        });

        Program.Check("Document.Load preserves structure and formatting basics", () =>
        {
            var doc = Document.Load(path);
            var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
            var title = paragraphs.First(p => p.GetText() == "Report Title");
            Program.AssertTrue(title.Style == ParagraphStyle.Title, "Title style survived");

            var mixed = paragraphs.First(p => p.GetText().Contains("bold red"));
            Program.AssertTrue(mixed.Runs.Count >= 3, $"runs preserved (got {mixed.Runs.Count})");
            var boldRun = mixed.Runs.First(r => r.TextContent == "bold red");
            Program.AssertTrue(boldRun.Format.Bold, "bold survived load");
            Program.AssertTrue(boldRun.Format.ColorHex == "FF0000", "color survived load");

            var bullet = paragraphs.First(p => p.GetText() == "Bullet item");
            Program.AssertTrue(bullet.List == ListKind.Bullet, "bullet kind survived");
            var numbered = paragraphs.First(p => p.GetText() == "Numbered item");
            Program.AssertTrue(numbered.List == ListKind.Number, "number kind survived");

            var table = doc.Blocks.OfType<DocTable>().First();
            Program.AssertTrue(table.Rows[1].Cells[0].GetText() == "Alpha", "table text survived");
        });

        Program.Check("Edit → save → re-read", () =>
        {
            var doc = Document.Load(path);
            doc.AddParagraph("Appended after load.");
            doc.SaveTo(path2);

            using var r = DocxReader.Open(path2);
            var text = r.ExtractText();
            Program.AssertContains(text, "Report Title", "original text kept");
            Program.AssertContains(text, "Appended after load.", "appended text present");
        });
    }
}

public static class TemplateTest
{
    public static void Run(string outDir)
    {
        var templatePath = Path.Combine(outDir, "template.docx");
        var filledPath = Path.Combine(outDir, "template-filled.docx");

        Program.Check("Build template incl. placeholder split across runs", () =>
        {
            var doc = new Document();
            doc.AddParagraph("Invoice for {{CustomerName}}", ParagraphStyle.Title);
            // Deliberately split a placeholder across three runs with different formats —
            // exactly what Word does after editing.
            doc.AddParagraph()
                .Text("Amount due: {{", TextFormat.None)
                .Text("Tot", TextFormat.BoldText)
                .Text("al}} by {{DueDate}}.");
            // Placeholder inside a table and the footer.
            var t = doc.AddTable(2);
            t.AddRow("Invoice No", "{{InvoiceNo}}");
            doc.Footer.SetText("Generated for {{CustomerName}}");
            // A placeholder we will NOT supply — must survive untouched.
            doc.AddParagraph("Notes: {{Unfilled}}");
            // A formatted paragraph with no placeholder — must keep its runs intact.
            doc.AddParagraph()
                .Text("Keep ")
                .Text("this bold", TextFormat.BoldText)
                .Text(" formatting.");
            doc.SaveTo(templatePath);
        });

        Program.Check("Fill template", () =>
        {
            DocxTemplate.Fill(templatePath, filledPath, new Dictionary<string, string>
            {
                ["CustomerName"] = "Acme Pvt Ltd",
                ["Total"] = "Rs 54,000.00",
                ["DueDate"] = "30 June 2026",
                ["InvoiceNo"] = "0042",
            });
            Program.AssertTrue(File.Exists(filledPath), "filled file missing");
        });

        Program.Check("Single-run placeholders replaced (body, table)", () =>
        {
            using var r = DocxReader.Open(filledPath);
            var text = r.ExtractText();
            Program.AssertContains(text, "Invoice for Acme Pvt Ltd", "title placeholder");
            Program.AssertContains(text, "0042", "table placeholder");
            Program.AssertTrue(!text.Contains("{{CustomerName}}"), "title placeholder leftover");
        });

        Program.Check("Run-spanning placeholder consolidated and replaced", () =>
        {
            using var r = DocxReader.Open(filledPath);
            var text = r.ExtractText();
            Program.AssertContains(text, "Amount due: Rs 54,000.00 by 30 June 2026.", "spanning replacement");
        });

        Program.Check("Footer placeholder replaced", () =>
        {
            var xml = Program.ReadPartXml(filledPath, "word/footer1.xml");
            Program.AssertContains(xml, "Acme Pvt Ltd", "footer replacement");
        });

        Program.Check("Unmatched placeholder left as-is", () =>
        {
            using var r = DocxReader.Open(filledPath);
            Program.AssertContains(r.ExtractText(), "{{Unfilled}}", "unmatched placeholder");
        });

        Program.Check("Formatting preserved in untouched paragraphs", () =>
        {
            var doc = Document.Load(filledPath);
            var p = doc.Blocks.OfType<Paragraph>().First(x => x.GetText().Contains("this bold"));
            Program.AssertTrue(p.Runs.Count >= 3, "untouched paragraph keeps separate runs");
            Program.AssertTrue(p.Runs.First(r => r.TextContent == "this bold").Format.Bold, "bold kept");
        });
    }
}

public static class ProtectionEncryptionTest
{
    public static void Run(string outDir)
    {
        var protectedPath = Path.Combine(outDir, "protected.docx");
        var encryptedPath = Path.Combine(outDir, "encrypted.docx");
        const string password = "S3cret-Chuvadi!";

        Program.Check("Restrict-editing protection writes documentProtection", () =>
        {
            var doc = new Document();
            doc.AddParagraph("Locked content.");
            doc.Protect("editpass", DocumentProtectionMode.ReadOnly);
            doc.SaveTo(protectedPath);

            var xml = Program.ReadPartXml(protectedPath, "word/settings.xml");
            Program.AssertContains(xml, "documentProtection", "protection element");
            Program.AssertContains(xml, "w:edit=\"readOnly\"", "readOnly mode");
            Program.AssertContains(xml, "w:enforcement=\"1\"", "enforcement");
            Program.AssertContains(xml, "cryptAlgorithmSid=\"14\"", "SHA-512 sid");
            Program.AssertTrue(!xml.Contains("editpass"), "plaintext password leaked");
        });

        Program.Check("Encrypted save produces a CFB container, not a zip", () =>
        {
            var doc = new Document();
            doc.AddHeading("Confidential", 1);
            doc.AddParagraph("The launch date is 14 August 2026.");
            var t = doc.AddTable(2);
            t.AddRow("Codename", "Olai");
            doc.SaveTo(encryptedPath, new EncryptionOptions { Password = password });

            var header = new byte[8];
            using var fs = File.OpenRead(encryptedPath);
            fs.ReadExactly(header);
            Program.AssertTrue(header[0] == 0xD0 && header[1] == 0xCF, "CFB magic missing");
        });

        Program.Check("Load with correct password round-trips", () =>
        {
            var doc = Document.Load(encryptedPath, password);
            var text = doc.GetText();
            Program.AssertContains(text, "launch date is 14 August 2026", "decrypted body");
            Program.AssertContains(text, "Olai", "decrypted table");
        });

        Program.Check("DocxReader opens encrypted file with password", () =>
        {
            using var r = DocxReader.Open(encryptedPath, password);
            Program.AssertContains(r.ExtractText(), "Confidential", "reader decryption");
        });

        Program.Check("Missing password throws DocxPasswordRequiredException", () =>
        {
            Program.AssertThrows<DocxPasswordRequiredException>(
                () => Document.Load(encryptedPath), "no password");
        });

        Program.Check("Wrong password throws DocxPasswordRequiredException", () =>
        {
            Program.AssertThrows<DocxPasswordRequiredException>(
                () => Document.Load(encryptedPath, "wrong-password"), "wrong password");
        });

        Program.Check("Unencrypted load ignores a supplied password", () =>
        {
            var doc = new Document();
            doc.AddParagraph("Open document.");
            var p = Path.Combine(outDir, "open.docx");
            doc.SaveTo(p);
            var loaded = Document.Load(p, "irrelevant");
            Program.AssertContains(loaded.GetText(), "Open document.", "password ignored");
        });

        Program.Check("Encrypted template fill (decrypt in → encrypt out)", () =>
        {
            var tpl = Path.Combine(outDir, "secure-template.docx");
            var filled = Path.Combine(outDir, "secure-filled.docx");
            var doc = new Document();
            doc.AddParagraph("Owner: {{Owner}}");
            doc.SaveTo(tpl, new EncryptionOptions { Password = "tpl-pass" });

            DocxTemplate.Fill(tpl, filled,
                new Dictionary<string, string> { ["Owner"] = "Arun" },
                password: "tpl-pass",
                outputEncryption: new EncryptionOptions { Password = "out-pass" });

            var result = Document.Load(filled, "out-pass");
            Program.AssertContains(result.GetText(), "Owner: Arun", "secure fill result");
        });
    }
}
