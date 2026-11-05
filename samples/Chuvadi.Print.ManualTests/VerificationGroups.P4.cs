using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>Phase 4 verification groups: the XPS writer (D-189).</summary>
    public static partial class VerificationGroups
    {
        private static Action[] P4Groups() => new Action[]
        {
            XpsSampleIsACompletePackage,
            XpsRefusesWhatItCannotExpress
        };

        public static void XpsSampleIsACompletePackage()
        {
            string? fontPath = Phase4Scenarios.FindSystemFont();
            IReadOnlyList<VectorPage> pages = Phase4Scenarios.SamplePages(fontPath);
            Require(XpsWriter.CanWrite(pages, out string? reason), "sample can be written: " + reason);
            var ms = new MemoryStream();
            XpsWriter.Write(pages, ms);

            using var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read);
            XNamespace x = XpsWriter.Namespace;
            XDocument Load(string name)
            {
                ZipArchiveEntry? e = zip.GetEntry(name);
                Require(e is not null, name + " is in the package");
                using Stream s = e!.Open();
                return XDocument.Load(s);
            }
            Require(Load("_rels/.rels").Root is not null, "package relationships");
            Require(Load("FixedDocSeq.fdseq").Root!.Name == x + "FixedDocumentSequence", "fixed document sequence");
            int pageCount = 0;
            foreach (XElement _ in Load("Documents/1/FixedDoc.fdoc").Root!.Elements(x + "PageContent")) pageCount++;
            Require(pageCount == 2, "two pages listed");
            Require(Load("Documents/1/Pages/1.fpage").Root!.Name == x + "FixedPage", "page 1");
            Require(Load("Documents/1/Pages/2.fpage").Root!.Name == x + "FixedPage", "page 2");

            if (fontPath is null) return;
            int fonts = 0;
            byte[] original = File.ReadAllBytes(fontPath);
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                if (!e.FullName.StartsWith("Resources/Fonts/", StringComparison.Ordinal)) continue;
                fonts++;
                Guid key = Guid.Parse(e.Name.Substring(0, 36));
                var stored = new MemoryStream();
                using (Stream s = e.Open()) s.CopyTo(stored);
                Require(SeqEqualBytes(XpsWriter.ObfuscateFont(stored.ToArray(), key), original), "font restores to the original file");
            }
            Require(fonts == 1, "the font is stored once");
        }

        public static void XpsRefusesWhatItCannotExpress()
        {
            var page = new VectorPage(100, 100);
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 10, 10))
            {
                Fill = new VectorSolidBrush(VectorColor.Black),
                BlendMode = VectorBlendMode.Multiply
            });
            Require(!XpsWriter.CanWrite(page, out string? reason) && reason!.Contains("Multiply", StringComparison.Ordinal), "blend mode refused: " + reason);
            bool threw = false;
            try { XpsWriter.Write(new[] { page }, new MemoryStream()); }
            catch (NotSupportedException) { threw = true; }
            Require(threw, "Write throws NotSupportedException");
        }
    }
}
