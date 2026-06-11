using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Fills <c>{{Placeholder}}</c> values into a .docx template designed in Word, preserving
/// EVERYTHING in the template — styles, images, tables, headers/footers, themes — because
/// only text nodes are rewritten; all other package parts are copied byte-for-byte.
///
/// <code>
/// DocxTemplate.Fill("invoice-template.docx", "invoice-0042.docx", new Dictionary&lt;string,string&gt;
/// {
///     ["CustomerName"] = "Acme Pvt Ltd",
///     ["InvoiceNo"]    = "0042",
///     ["Total"]        = "₹54,000.00",
/// });
/// </code>
///
/// Placeholders are replaced in the body, headers, footers, footnotes and endnotes.
/// Word often splits typed text across multiple runs (spell-check marks, formatting edits),
/// so a placeholder may span runs; this is handled by consolidating the paragraph's text
/// into its first run — at the cost of mixed per-run formatting WITHIN such a paragraph.
/// Placeholders fully contained in a single run keep all surrounding formatting intact.
/// A placeholder must not span paragraphs. Unmatched placeholders are left as-is.
/// </summary>
public static class DocxTemplate
{
    private const string W = DocumentSerializer.W;

    /// <summary>Fills a template file into a new output file.</summary>
    /// <param name="inputPath">The template .docx (may be password-encrypted; pass <paramref name="password"/>).</param>
    /// <param name="outputPath">The filled output (created or overwritten).</param>
    /// <param name="values">Placeholder name → replacement text. Names are matched case-sensitively
    /// as <c>{{Name}}</c>.</param>
    /// <param name="password">Password for an encrypted template; null for unencrypted.</param>
    /// <param name="outputEncryption">When set, the filled output is saved encrypted.</param>
    public static void Fill(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> values,
        string? password = null,
        EncryptionOptions? outputEncryption = null)
    {
        if (string.IsNullOrEmpty(inputPath)) throw new ArgumentException("Input path required.", nameof(inputPath));
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path required.", nameof(outputPath));
        if (values is null) throw new ArgumentNullException(nameof(values));

        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Obtain the plaintext package (decrypting when needed).
        Stream packageStream;
        MemoryStream? decrypted = null;
        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(input))
        {
            byte[] plaintext;
            try
            {
                plaintext = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(input, password);
            }
            catch (Chuvadi.Internal.Crypto.PackagePasswordException ex)
            {
                throw new DocxPasswordRequiredException(ex.Message, ex);
            }
            decrypted = new MemoryStream(plaintext);
            packageStream = decrypted;
        }
        else
        {
            packageStream = input;
        }

        try
        {
            if (outputEncryption is null)
            {
                using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                FillPackage(packageStream, output, values);
            }
            else
            {
                if (string.IsNullOrEmpty(outputEncryption.Password))
                    throw new ArgumentException("Encryption password cannot be empty.", nameof(outputEncryption));

                var tempPath = Path.Combine(Path.GetTempPath(), $"chuvadi_docs_tpl_{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var spool = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                        FillPackage(packageStream, spool, values);
                    using var spoolRead = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    Chuvadi.Internal.Crypto.EncryptedPackageWriter.WriteEncrypted(
                        output, spoolRead, spoolRead.Length, outputEncryption.Password, outputEncryption.SpinCount);
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
        }
        finally
        {
            decrypted?.Dispose();
        }
    }

    // ---- Package transform: copy every entry, rewriting only the text-bearing XML parts ----

    private static void FillPackage(Stream packageIn, Stream packageOut, IReadOnlyDictionary<string, string> values)
    {
        using var zipIn = new ZipArchive(packageIn, ZipArchiveMode.Read, leaveOpen: true);
        using var zipOut = new ZipArchive(packageOut, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var entry in zipIn.Entries)
        {
            var outEntry = zipOut.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            using var src = entry.Open();
            using var dest = outEntry.Open();

            if (IsTextBearingPart(entry.FullName))
            {
                var xml = new XmlDocument { PreserveWhitespace = true };
                using (var reader = XmlReader.Create(src, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    CloseInput = false,
                }))
                {
                    xml.Load(reader);
                }
                ReplaceInDocument(xml, values);
                using var writer = XmlWriter.Create(dest, new XmlWriterSettings
                {
                    Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    CloseOutput = false,
                });
                xml.Save(writer);
            }
            else
            {
                src.CopyTo(dest);
            }
        }
    }

    private static bool IsTextBearingPart(string entryName)
        => entryName is "word/document.xml" or "word/footnotes.xml" or "word/endnotes.xml"
           || (entryName.StartsWith("word/header", StringComparison.Ordinal) && entryName.EndsWith(".xml", StringComparison.Ordinal))
           || (entryName.StartsWith("word/footer", StringComparison.Ordinal) && entryName.EndsWith(".xml", StringComparison.Ordinal));

    private static void ReplaceInDocument(XmlDocument xml, IReadOnlyDictionary<string, string> values)
    {
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("w", W);

        var paragraphs = xml.SelectNodes("//w:p", ns);
        if (paragraphs is null) return;

        foreach (XmlNode p in paragraphs)
        {
            var textNodes = p.SelectNodes(".//w:t", ns);
            if (textNodes is null || textNodes.Count == 0) continue;

            // Pass 1 — placeholders fully inside a single w:t: replace in place, keeping
            // every run's formatting untouched.
            foreach (XmlNode t in textNodes)
                t.InnerText = ReplaceAll(t.InnerText, values);

            // Pass 2 — placeholders split ACROSS runs: detect "{{...}}" spanning the
            // paragraph's concatenated text and, only then, consolidate the text into the
            // FIRST w:t (its run's formatting wins) and blank the rest.
            var combined = ConcatText(textNodes);
            if (ContainsCompletePlaceholder(combined))
            {
                var replaced = ReplaceAll(combined, values);
                if (replaced != combined) // only consolidate when something actually matched
                {
                    var first = (XmlElement)textNodes[0]!;
                    first.InnerText = replaced;
                    first.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
                    for (int i = 1; i < textNodes.Count; i++)
                        textNodes[i]!.InnerText = string.Empty;
                }
            }
        }
    }

    private static string ConcatText(XmlNodeList textNodes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode t in textNodes) sb.Append(t.InnerText);
        return sb.ToString();
    }

    private static bool ContainsCompletePlaceholder(string s)
    {
        int open = s.IndexOf("{{", StringComparison.Ordinal);
        if (open < 0) return false;
        return s.IndexOf("}}", open + 2, StringComparison.Ordinal) >= 0;
    }

    private static string ReplaceAll(string text, IReadOnlyDictionary<string, string> values)
    {
        if (text.IndexOf("{{", StringComparison.Ordinal) < 0) return text;
        foreach (var (key, value) in values)
            text = text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        return text;
    }
}
