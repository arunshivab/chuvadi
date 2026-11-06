using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.0 error contract (D-136 option a): every malformed-input failure from Document.Load,
/// DocxReader (Open and its lazy readers) and DocxTemplate (Fill, GetPlaceholders, CheckTemplate)
/// is a <see cref="DocxFormatException"/> carrying the original error as InnerException;
/// DocxSecurityLimitException, DocxPasswordRequiredException, DocxTemplateException and
/// OperationCanceledException pass through unchanged; caller mistakes keep their own types.
/// Also the Fill bug fix: the template entry points check the package the way Load and Open do.
/// </summary>
public static class ErrorContractTests
{
    public static void Run(string outDir)
    {
        var dir = Path.Combine(outDir, "errors");
        Directory.CreateDirectory(dir);
        Matrix(dir);
        TemplatePackageCheck(dir);
        MainPartMissing(dir);
        PassThrough(dir);
        CallerMistakes(dir);
    }

    // ---- every entry point x every malformed case ------------------------------------------

    /// <summary>The cells that read successfully; every other malformed cell throws DocxFormatException.</summary>
    private static readonly Dictionary<string, string[]> Succeeds = BuildSucceeds();

    private static Dictionary<string, string[]> BuildSucceeds()
    {
        var load = new[] { "header not well-formed" };
        var open = new[] { "document.xml not well-formed", "styles.xml not well-formed", "numbering.xml not well-formed", "header not well-formed" };
        var text = new[] { "styles.xml not well-formed", "numbering.xml not well-formed", "header not well-formed" };   // D-194: a broken header reads as blank
        var templates = new[] { "styles.xml not well-formed", "numbering.xml not well-formed" };                         // D-194: templates stay strict
        var images = new[] { "styles.xml not well-formed", "numbering.xml not well-formed", "header not well-formed" };
        var active = Array.Empty<string>();
        var d = new Dictionary<string, string[]>();
        foreach (var e in new[] { "Document.Load(stream)", "Document.Load(stream, password, options, ct)", "Document.Load(path)", "Document.Load(path, password)", "Document.Load(path, password, options, ct)" }) d[e] = load;
        foreach (var e in new[] { "DocxReader.Open(stream)", "DocxReader.Open(path)", "DocxReader.HasMacros" }) d[e] = open;
        foreach (var e in new[] { "DocxReader.Paragraphs()", "DocxReader.Tables()", "DocxReader.ExtractText()" }) d[e] = text;
        foreach (var e in new[] { "DocxTemplate.Fill(stream, stream, data)", "DocxTemplate.Fill(path, path, data)", "DocxTemplate.Fill(path, path, values)",
                                  "DocxTemplate.Fill(path, path, text, images, ..., ct)", "DocxTemplate.GetPlaceholders(stream)", "DocxTemplate.GetPlaceholders(path)",
                                  "DocxTemplate.CheckTemplate(stream)", "DocxTemplate.CheckTemplate(path)" }) d[e] = templates;
        foreach (var e in new[] { "DocxReader.Images()", "DocxReader.SaveImages(folder)" }) d[e] = images;
        d["DocxReader.GetActiveContent()"] = active;
        return d;
    }

    private static void Matrix(string dir)
    {
        var clean = ErrorContractMatrix.BaseDocument();
        var cases = ErrorContractMatrix.Cases(clean);
        var entries = ErrorContractMatrix.EntryPoints(dir);
        Check($"the matrix covers {entries.Count} entry points x {cases.Count} malformed cases, and every entry point has expectations", () =>
        {
            AssertTrue(entries.Count == 22 && cases.Count == 14, $"{entries.Count} entry points, {cases.Count} cases");
            foreach (var (name, _) in entries) AssertTrue(Succeeds.ContainsKey(name), "no expectation for " + name);
        });
        foreach (var (entry, run) in entries)
        {
            Check($"{entry}: a clean package reads; every malformed case is DocxFormatException (original as InnerException) or reads", () =>
            {
                AssertTrue(Outcome(run, clean) is null, "clean package failed: " + Describe(run, clean));
                foreach (var (name, bytes) in cases)
                {
                    var ex = Outcome(run, bytes);
                    bool shouldSucceed = Succeeds[entry].Contains(name);
                    if (shouldSucceed) { AssertTrue(ex is null, $"{name}: expected to read, got {Describe(ex)}"); continue; }
                    AssertTrue(ex is DocxFormatException && ex is not DocxSecurityLimitException, $"{name}: expected DocxFormatException, got {Describe(ex)}");
                    // A wrapped failure keeps the original; a failure our own checks raise has none.
                    AssertTrue(ex!.InnerException is null or not DocxFormatException, $"{name}: a contract exception was wrapped: {Describe(ex)}");
                }
            });
        }
        Check("the wrapped original is reachable: not-well-formed XML -> InnerException XmlException; not a zip -> InnerException InvalidDataException", () =>
        {
            var badXml = cases.Single(c => c.Name == "document.xml not well-formed").Bytes;
            var ex = Outcome(b => Document.Load(new MemoryStream(b)), badXml);
            AssertTrue(ex is DocxFormatException { InnerException: System.Xml.XmlException }, Describe(ex));
            ex = Outcome(b => { using var r = DocxReader.Open(new MemoryStream(b)); r.ExtractText(); }, badXml);
            AssertTrue(ex is DocxFormatException { InnerException: System.Xml.XmlException }, Describe(ex));
            ex = Outcome(b => DocxTemplate.Fill(new MemoryStream(b), new MemoryStream(), new TemplateData()), badXml);
            AssertTrue(ex is DocxFormatException { InnerException: System.Xml.XmlException }, Describe(ex));
            var garbage = cases.Single(c => c.Name == "garbage bytes").Bytes;
            ex = Outcome(b => Document.Load(new MemoryStream(b)), garbage);
            AssertTrue(ex is DocxFormatException { InnerException: InvalidDataException }, Describe(ex));
            ex = Outcome(b => DocxTemplate.GetPlaceholders(new MemoryStream(b)), garbage);
            AssertTrue(ex is DocxFormatException { InnerException: InvalidDataException }, Describe(ex));
            AssertTrue(ex!.Message.StartsWith("The template is not a valid docx package:", StringComparison.Ordinal), ex.Message);
        });
        Check("lazy readers wrap while enumerating: Paragraphs()/Tables() are lazy, the failure comes from MoveNext", () =>
        {
            var badXml = cases.Single(c => c.Name == "document.xml not well-formed").Bytes;
            using var r = DocxReader.Open(new MemoryStream(badXml));
            var paragraphs = r.Paragraphs();   // nothing read yet
            var tables = r.Tables();
            AssertThrows<DocxFormatException>(() => paragraphs.GetEnumerator().MoveNext(), "Paragraphs MoveNext");
            AssertThrows<DocxFormatException>(() => tables.GetEnumerator().MoveNext(), "Tables MoveNext");
        });
    }

    // ---- the Fill bug fix ----------------------------------------------------------------

    // ---- D-184: the main part must exist ------------------------------------------------

    private const string MainPartMissingMessage = "The main document part '/word/document.xml' is missing from the package.";

    private static void MainPartMissing(string dir)
    {
        var clean = ErrorContractMatrix.BaseDocument();
        var noMain = ErrorContractMatrix.Remove(clean, "word/document.xml");
        Check("D-184: a package without word/document.xml (still declared) is rejected by every entry point with the one message, and Fill writes nothing", () =>
        {
            foreach (var (entry, run) in ErrorContractMatrix.EntryPoints(dir))
            {
                var ex = Outcome(run, noMain);
                AssertTrue(ex is not null && ex.GetType() == typeof(DocxFormatException) && ex.Message == MainPartMissingMessage && ex.InnerException is null,
                    $"{entry}: {Describe(ex)}");
            }
            var output = new MemoryStream();
            AssertThrows<DocxFormatException>(() => DocxTemplate.Fill(new MemoryStream(noMain), output, new TemplateData().Set("Name", "N")), "Fill(stream)");
            AssertTrue(output.Length == 0, "Fill wrote " + output.Length + " bytes");
            var input = Path.Combine(dir, "no-main.docx");
            var outPath = Path.Combine(dir, "no-main-filled.docx");
            File.WriteAllBytes(input, noMain);
            if (File.Exists(outPath)) File.Delete(outPath);
            AssertThrows<DocxFormatException>(() => DocxTemplate.Fill(input, outPath, new System.Collections.Generic.Dictionary<string, string> { ["Name"] = "N" }), "Fill(path)");
            AssertTrue(!File.Exists(outPath), "Fill created the output");
        });
        Check("D-184: missing optional parts are still skipped: a declared header, footer, numbering or styles part with no zip entry still opens and reads the body", () =>
        {
            foreach (var part in new[] { ErrorContractMatrix.HeaderPart(clean), FooterPart(clean), "word/numbering.xml", "word/styles.xml" })
            {
                var missing = ErrorContractMatrix.Remove(clean, part);
                AssertTrue(Document.Load(new MemoryStream(missing)).GetText().Contains("Body {{Name}}"), part + ": Load");
                using var r = DocxReader.Open(new MemoryStream(missing));
                AssertTrue(r.ExtractText().Contains("Body {{Name}}"), part + ": ExtractText");
                AssertTrue(DocxTemplate.GetPlaceholders(new MemoryStream(missing)).Count >= 1, part + ": GetPlaceholders");
                var output = new MemoryStream();
                DocxTemplate.Fill(new MemoryStream(missing), output, new TemplateData().Set("Name", "N"));
                AssertTrue(output.Length > 0, part + ": Fill");
            }
        });
    }

    private static string FooterPart(byte[] package)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).First(n => n.StartsWith("word/footer", StringComparison.Ordinal));
    }

    private static void TemplatePackageCheck(string dir)
    {
        var clean = ErrorContractMatrix.BaseDocument();
        var broken = ErrorContractMatrix.Replace(clean, "[Content_Types].xml", "<Types><Default");
        var noMain = ErrorContractMatrix.Edit(clean, "[Content_Types].xml", s => s.Replace(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
        Check("bug fix: Fill rejects a broken [Content_Types].xml (not well-formed, or a non-Word main part) like Load and Open, and writes nothing", () =>
        {
            foreach (var bad in new[] { broken, noMain })
            {
                AssertThrows<DocxFormatException>(() => Document.Load(new MemoryStream(bad)), "Load");
                AssertThrows<DocxFormatException>(() => DocxReader.Open(new MemoryStream(bad)), "Open");
                var output = new MemoryStream();
                AssertThrows<DocxFormatException>(() => DocxTemplate.Fill(new MemoryStream(bad), output, new TemplateData().Set("Name", "N")), "Fill(stream)");
                AssertTrue(output.Length == 0, "Fill wrote " + output.Length + " bytes");
                var input = Path.Combine(dir, "ct-broken.docx");
                var outPath = Path.Combine(dir, "ct-broken-filled.docx");
                File.WriteAllBytes(input, bad);
                if (File.Exists(outPath)) File.Delete(outPath);
                AssertThrows<DocxFormatException>(() => DocxTemplate.Fill(input, outPath, new Dictionary<string, string> { ["Name"] = "N" }), "Fill(path)");
                AssertTrue(!File.Exists(outPath), "Fill created the output");
            }
        });
        Check("bug fix: GetPlaceholders and CheckTemplate reject the same packages (they listed placeholders from them before)", () =>
        {
            foreach (var bad in new[] { broken, noMain })
            {
                AssertThrows<DocxFormatException>(() => DocxTemplate.GetPlaceholders(new MemoryStream(bad)), "GetPlaceholders");
                AssertThrows<DocxFormatException>(() => DocxTemplate.CheckTemplate(new MemoryStream(bad)), "CheckTemplate");
            }
        });
        Check("a clean template still fills, lists and checks; a .dotx (template main part) is still accepted", () =>
        {
            var output = new MemoryStream();
            DocxTemplate.Fill(new MemoryStream(clean), output, new TemplateData().Set("Name", "Ada"));
            AssertTrue(Document.Load(new MemoryStream(output.ToArray())).GetText().Contains("Body Ada"), "filled");
            AssertTrue(DocxTemplate.GetPlaceholders(new MemoryStream(clean)).Count >= 2, "placeholders");
            AssertTrue(DocxTemplate.CheckTemplate(new MemoryStream(clean)).Count == 0, "no problems");
            var dotx = ErrorContractMatrix.Edit(clean, "[Content_Types].xml", s => s.Replace(
                "wordprocessingml.document.main+xml", "wordprocessingml.template.main+xml"));
            AssertTrue(DocxTemplate.GetPlaceholders(new MemoryStream(dotx)).Count >= 2, "dotx placeholders");
            DocxTemplate.Fill(new MemoryStream(dotx), new MemoryStream(), new TemplateData().Set("Name", "Ada"));
        });
    }

    // ---- contract exceptions pass through unchanged --------------------------------------

    private static void PassThrough(string dir)
    {
        var clean = ErrorContractMatrix.BaseDocument();
        var encrypted = new MemoryStream();
        Document.Load(new MemoryStream(clean)).SaveTo(encrypted, new EncryptionOptions { Password = ErrorContractMatrix.Password, SpinCount = 1000 });
        var enc = encrypted.ToArray();

        Check("DocxSecurityLimitException passes through unchanged (not wrapped) from Load, Open, lazy readers and every template entry point", () =>
        {
            var tiny = new DocxReaderOptions { MaxPackageBytes = 100 };
            ExactlyLimit(() => Document.Load(new MemoryStream(clean), null, tiny), "Load");
            ExactlyLimit(() => DocxReader.Open(new MemoryStream(clean), null, tiny), "Open");
            ExactlyLimit(() => DocxTemplate.Fill(new MemoryStream(clean), new MemoryStream(), new TemplateData(), new TemplateOptions { ReadOptions = tiny }), "Fill");
            ExactlyLimit(() => DocxTemplate.GetPlaceholders(new MemoryStream(clean), null, tiny), "GetPlaceholders");
            ExactlyLimit(() => DocxTemplate.CheckTemplate(new MemoryStream(clean), null, tiny), "CheckTemplate");
            // D-206: Open reads the whole main part once, so a main part over MaxPartBytes is reported at Open.
            ExactlyLimit(() => DocxReader.Open(new MemoryStream(clean), null, new DocxReaderOptions { MaxPartBytes = 64 }), "Open (main part size)");
            // A header over the limit is still reported when it is read (the main part is within it).
            var bigHeader = ErrorContractMatrix.Replace(clean, ErrorContractMatrix.HeaderPart(clean),
                "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>" + new string('h', 200_000) + "</w:t></w:r></w:p></w:hdr>");
            using var r = DocxReader.Open(new MemoryStream(bigHeader), null, new DocxReaderOptions { MaxPartBytes = 100_000, IncludeHeadersAndFooters = true });
            ExactlyLimit(() => r.Paragraphs().ToList(), "Paragraphs (header part size)");
            ExactlyLimit(() => r.ExtractText(), "ExtractText (header part size)");
        });
        Check("DocxPasswordRequiredException passes through unchanged (no or wrong password) from Load, Open and every template entry point", () =>
        {
            foreach (var pw in new string?[] { null, "wrong" })
            {
                Exactly<DocxPasswordRequiredException>(() => Document.Load(new MemoryStream(enc), pw), "Load " + pw);
                Exactly<DocxPasswordRequiredException>(() => DocxReader.Open(new MemoryStream(enc), pw), "Open " + pw);
                Exactly<DocxPasswordRequiredException>(() => DocxTemplate.Fill(new MemoryStream(enc), new MemoryStream(), new TemplateData(), new TemplateOptions { Password = pw }), "Fill " + pw);
                Exactly<DocxPasswordRequiredException>(() => DocxTemplate.GetPlaceholders(new MemoryStream(enc), pw), "GetPlaceholders " + pw);
                Exactly<DocxPasswordRequiredException>(() => DocxTemplate.CheckTemplate(new MemoryStream(enc), pw), "CheckTemplate " + pw);
            }
            AssertTrue(Document.Load(new MemoryStream(enc), ErrorContractMatrix.Password).GetText().Contains("Body"), "right password reads");
        });
        Check("OperationCanceledException passes through unchanged from Load, Open, the lazy readers and every template entry point", () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ct = cts.Token;
            Canceled(() => Document.Load(new MemoryStream(clean), null, null, ct), "Load");
            Canceled(() => Document.Load(new MemoryStream(enc), ErrorContractMatrix.Password, null, ct), "Load encrypted");
            Canceled(() => DocxReader.Open(new MemoryStream(clean), null, null, ct), "Open");
            using var r = DocxReader.Open(new MemoryStream(clean));
            Canceled(() => r.Paragraphs(ct).ToList(), "Paragraphs");
            Canceled(() => r.Tables(ct).ToList(), "Tables");
            Canceled(() => r.ExtractText(ct), "ExtractText");
            Canceled(() => r.Images(ct), "Images");
            Canceled(() => r.GetActiveContent(ct), "GetActiveContent");
            Canceled(() => DocxTemplate.Fill(new MemoryStream(clean), new MemoryStream(), new TemplateData(), null, ct), "Fill");
            Canceled(() => DocxTemplate.GetPlaceholders(new MemoryStream(clean), null, null, ct), "GetPlaceholders");
            Canceled(() => DocxTemplate.CheckTemplate(new MemoryStream(clean), null, null, ct), "CheckTemplate");
        });
        Check("DocxTemplateException passes through unchanged from Fill (a strict fill with a missing value; too many repeated items)", () =>
        {
            Exactly<DocxTemplateException>(() => DocxTemplate.Fill(new MemoryStream(clean), new MemoryStream(), new TemplateData(),
                new TemplateOptions { Strict = true }), "strict, missing value");
            var d = new Document();
            d.AddParagraph("{{#Items}}");
            d.AddParagraph("{{Name}}");
            d.AddParagraph("{{/Items}}");
            var ms = new MemoryStream();
            d.SaveTo(ms);
            var data = new TemplateData().SetItems("Items", new[] { new TemplateData().Set("Name", "a"), new TemplateData().Set("Name", "b") });
            Exactly<DocxTemplateException>(() => DocxTemplate.Fill(new MemoryStream(ms.ToArray()), new MemoryStream(), data,
                new TemplateOptions { MaxGeneratedItems = 1 }), "MaxGeneratedItems");
        });
        Check("ObjectDisposedException passes through unchanged from a disposed reader", () =>
        {
            var r = DocxReader.Open(new MemoryStream(clean));
            r.Dispose();
            Exactly<ObjectDisposedException>(() => r.Paragraphs(), "Paragraphs");
            Exactly<ObjectDisposedException>(() => r.Tables(), "Tables");
            Exactly<ObjectDisposedException>(() => r.ExtractText(), "ExtractText");
            Exactly<ObjectDisposedException>(() => r.Images(), "Images");
            Exactly<ObjectDisposedException>(() => r.GetActiveContent(), "GetActiveContent");
            Exactly<ObjectDisposedException>(() => _ = r.ContainsVbaProject, "ContainsVbaProject");
        });
        Check("DocxLimitExceededException (an IOException) is never wrapped, unlike other IOExceptions from the caller's stream", () =>
        {
            var limit = new DocxLimitExceededException("MaxInputChars", 10, 11, "the text input is longer than allowed.");
            AssertTrue(limit is IOException && limit.Message == "MaxInputChars exceeded (11 > 10): the text input is longer than allowed.", limit.Message);
            var ex = Outcome(_ => Document.Load(new FailingStream(clean, () => new DocxLimitExceededException("MaxInputChars", 10, 11, "test."))), clean);
            AssertTrue(ex is DocxLimitExceededException && ex.GetType() == typeof(DocxLimitExceededException), "Load: " + Describe(ex));
            ex = Outcome(_ => DocxTemplate.Fill(new FailingStream(clean, () => new DocxLimitExceededException("MaxInputChars", 10, 11, "test.")), new MemoryStream(), new TemplateData()), clean);
            AssertTrue(ex is DocxLimitExceededException, "Fill: " + Describe(ex));
        });
        Check("a read error from the caller's stream is reported as DocxFormatException with the IOException as InnerException", () =>
        {
            var ex = Outcome(_ => Document.Load(new FailingStream(clean)), clean);
            AssertTrue(ex is DocxFormatException { InnerException: IOException }, Describe(ex));
            ex = Outcome(_ => DocxTemplate.Fill(new FailingStream(clean), new MemoryStream(), new TemplateData()), clean);
            AssertTrue(ex is DocxFormatException { InnerException: IOException }, Describe(ex));
        });
    }

    // ---- caller mistakes keep their own exception types ---------------------------------

    private static void CallerMistakes(string dir)
    {
        var clean = ErrorContractMatrix.BaseDocument();
        Check("argument and file-system errors are not wrapped: null/empty arguments, a missing file, an empty output password", () =>
        {
            Exactly<ArgumentNullException>(() => Document.Load((Stream)null!), "Load(null stream)");
            Exactly<ArgumentException>(() => Document.Load(""), "Load(\"\")");
            Exactly<ArgumentNullException>(() => DocxReader.Open((Stream)null!), "Open(null stream)");
            Exactly<ArgumentNullException>(() => DocxTemplate.GetPlaceholders((Stream)null!), "GetPlaceholders(null stream)");
            Exactly<ArgumentNullException>(() => DocxTemplate.CheckTemplate((Stream)null!), "CheckTemplate(null stream)");
            Exactly<ArgumentNullException>(() => DocxTemplate.Fill(new MemoryStream(clean), new MemoryStream(), (TemplateData)null!), "Fill(null data)");
            var missing = Path.Combine(dir, "does-not-exist.docx");
            Exactly<FileNotFoundException>(() => Document.Load(missing), "Load(missing)");
            Exactly<FileNotFoundException>(() => DocxReader.Open(missing), "Open(missing)");
            Exactly<FileNotFoundException>(() => DocxTemplate.GetPlaceholders(missing), "GetPlaceholders(missing)");
            Exactly<FileNotFoundException>(() => DocxTemplate.Fill(missing, Path.Combine(dir, "x.docx"), new TemplateData()), "Fill(missing)");
            Exactly<ArgumentException>(() => DocxTemplate.Fill(new MemoryStream(clean), new MemoryStream(), new TemplateData(),
                new TemplateOptions { OutputEncryption = new EncryptionOptions { Password = "" } }), "Fill(empty output password)");
        });
        Check("SaveImages: a file-system failure while writing is not wrapped (only reading is)", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(clean));
            var blocker = Path.Combine(dir, "not-a-folder");
            File.WriteAllText(blocker, "x");
            var ex = Outcome(_ => r.SaveImages(blocker), clean);
            AssertTrue(ex is IOException && ex is not DocxFormatException, Describe(ex));
        });
    }

    // ---- helpers ------------------------------------------------------------------------

    private static Exception? Outcome(Action<byte[]> run, byte[] bytes)
    {
        try { run(bytes); return null; }
        catch (Exception ex) { return ex; }
    }

    private static string Describe(Action<byte[]> run, byte[] bytes) => Describe(Outcome(run, bytes));

    private static string Describe(Exception? ex) => ex is null ? "nothing thrown"
        : ex.GetType().Name + (ex.InnerException is null ? "" : "(" + ex.InnerException.GetType().Name + ")") + ": " + ex.Message;

    private static void Exactly<T>(Action action, string context) where T : Exception
    {
        var ex = Outcome(_ => action(), Array.Empty<byte>());
        AssertTrue(ex is not null && ex.GetType() == typeof(T), $"{context}: expected exactly {typeof(T).Name}, got {Describe(ex)}");
    }

    private static void ExactlyLimit(Action action, string context) => Exactly<DocxSecurityLimitException>(action, context);

    private static void Canceled(Action action, string context)
    {
        var ex = Outcome(_ => action(), Array.Empty<byte>());
        AssertTrue(ex is OperationCanceledException, $"{context}: expected OperationCanceledException, got {Describe(ex)}");
    }

    /// <summary>A readable, seekable stream that fails part way through reading.</summary>
    private sealed class FailingStream : MemoryStream
    {
        private readonly Func<Exception> _failure;
        public FailingStream(byte[] bytes, Func<Exception>? failure = null) : base(bytes, writable: false)
            => _failure = failure ?? (() => new IOException("Simulated read failure."));
        public override int Read(byte[] buffer, int offset, int count) => Position > 200 ? throw _failure() : base.Read(buffer, offset, Math.Min(count, 100));
        public override int Read(Span<byte> buffer) => Position > 200 ? throw _failure() : base.Read(buffer[..Math.Min(buffer.Length, 100)]);
    }
}
