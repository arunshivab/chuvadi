using System.Text;
using Chuvadi.Docs.Model;
using Chuvadi.Layout.Documents;
using Chuvadi.Layout.Tests;

namespace Chuvadi.Layout.Documents.Tests;

/// <summary>Format detection: one call for docx, xlsx, encrypted files of either kind and text (D-115).</summary>
public class AutoDetectLayoutTests
{
    private static LayoutOptions Options() => Flow.Options();

    private static byte[] Save(Document doc)
    {
        using MemoryStream ms = new MemoryStream();
        doc.SaveTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Open_sniffs_docx_and_encrypted_documents()
    {
        Document doc = new Document();
        doc.AddParagraph("Sniffed and laid out");
        using LaidOutDocument laid = AutoDetectLayout.Open(new MemoryStream(Save(doc)), LayoutSourceFormat.Auto, Options());
        Assert.Equal(LayoutSourceFormat.Docx, laid.Source.Format);
        Assert.Contains("Sniffed and laid out", laid.GetPage(0).GetText());

        // The same document, encrypted with password "pw" (Docs tests' Fixtures/encrypted/sniff-pw.docx).
        byte[] encrypted = EncryptedFixtures.Open("sniff-pw.docx").ToArray();
        Assert.False(Assert.Throws<LayoutPasswordRequiredException>(() => AutoDetectLayout.Open(new MemoryStream(encrypted), LayoutSourceFormat.Auto, Options())).PasswordGiven);
        LayoutOptions withPassword = Options();
        withPassword.Password = "pw";
        using LaidOutDocument opened = AutoDetectLayout.Open(new MemoryStream(encrypted), LayoutSourceFormat.Auto, withPassword);
        Assert.Equal(LayoutSourceFormat.Docx, opened.Source.Format);
        LayoutOptions wrong = Options();
        wrong.Password = "nope";
        Assert.True(Assert.Throws<LayoutPasswordRequiredException>(() => AutoDetectLayout.Open(new MemoryStream(encrypted), LayoutSourceFormat.Auto, wrong)).PasswordGiven);

        // An encrypted workbook with the same entry point still opens as a workbook.
        using LaidOutDocument book = AutoDetectLayout.Open(EncryptedFixtures.Open("sample-pw.xlsx"), LayoutSourceFormat.Auto, withPassword);
        Assert.Equal(LayoutSourceFormat.Xlsx, book.Source.Format);
    }

    [Fact]
    public void Text_markdown_and_html_go_where_the_engine_sends_them()
    {
        using LaidOutDocument text = AutoDetectLayout.Open(Encoding.UTF8.GetBytes("plain words"), LayoutSourceFormat.Auto, Options());
        Assert.Equal(LayoutSourceFormat.Text, text.Source.Format);
        Assert.Contains("plain words", text.GetPage(0).GetText());
        LayoutFormatException md = Assert.Throws<LayoutFormatException>(() => AutoDetectLayout.Open(Encoding.UTF8.GetBytes("# h"), LayoutSourceFormat.Markdown, Options()));
        Assert.Contains("Chuvadi.Layout.Markup", md.Message);
        Assert.Throws<LayoutFormatException>(() => AutoDetectLayout.Open(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 }, LayoutSourceFormat.Auto, Options()));
        // Sniffed as a document, then rejected cleanly by the docx importer (not a real package).
        byte[] docxLike = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(Encoding.ASCII.GetBytes("....word/document.xml")).ToArray();
        LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => AutoDetectLayout.Open(docxLike, LayoutSourceFormat.Auto, Options()));
        Assert.Contains("document could not be read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_engine_names_the_packages_for_office_files()
    {
        Document doc = new Document();
        doc.AddParagraph("x");
        byte[] docx = Save(doc);
        LayoutFormatException d = Assert.Throws<LayoutFormatException>(() => DocumentLayout.Open(new MemoryStream(docx), LayoutSourceFormat.Auto, Options()));
        Assert.Contains("Chuvadi.Layout.Docs", d.Message);
        Assert.Contains("AutoDetectLayout", d.Message);
        LayoutOptions withPassword = Options();
        withPassword.Password = "pw";
        LayoutFormatException e = Assert.Throws<LayoutFormatException>(() => DocumentLayout.Open(EncryptedFixtures.Open("sniff-pw.docx"), LayoutSourceFormat.Auto, withPassword));
        Assert.Contains("Chuvadi.Layout.Documents", e.Message);
        LayoutFormatException x = Assert.Throws<LayoutFormatException>(() => DocumentLayout.Open(EncryptedFixtures.Open("sample-pw.xlsx"), LayoutSourceFormat.Xlsx, withPassword));
        Assert.Contains("Chuvadi.Layout.Sheets", x.Message);
    }

    [Fact]
    public void Limits_and_the_input_are_respected()
    {
        LayoutOptions tight = Options();
        tight.Limits = new LayoutLimits { MaxSourceBytes = 4 };
        Assert.Throws<LayoutLimitExceededException>(() => AutoDetectLayout.Open(Encoding.UTF8.GetBytes("too long"), LayoutSourceFormat.Auto, tight));
        Assert.Throws<LayoutLimitExceededException>(() => AutoDetectLayout.Open(new MemoryStream(Encoding.UTF8.GetBytes("too long")), LayoutSourceFormat.Auto, tight));
        byte[] data = Encoding.UTF8.GetBytes("unchanged");
        byte[] copy = (byte[])data.Clone();
        MemoryStream ms = new MemoryStream(data, writable: false);
        using LaidOutDocument doc = AutoDetectLayout.Open(ms, LayoutSourceFormat.Auto, Options());
        Assert.True(ms.CanRead);
        Assert.Equal(copy, data);
    }
}
