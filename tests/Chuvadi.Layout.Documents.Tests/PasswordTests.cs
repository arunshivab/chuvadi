using Chuvadi.Docs.Model;
using Chuvadi.Layout.Docs;
using Chuvadi.Layout.Documents;
using Chuvadi.Layout.Sheets;
using Chuvadi.Layout.Tests;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Layout.Documents.Tests;

/// <summary>
/// D-253: a missing or wrong password is <see cref="LayoutPasswordRequiredException"/> from every entry point, with
/// <see cref="LayoutPasswordRequiredException.PasswordGiven"/> telling the two apart. Fixtures: sniff-pw.docx and
/// sample-pw.xlsx, both encrypted with "pw", read from memory only.
/// </summary>
public class PasswordTests
{
    private static LayoutOptions With(string? password)
    {
        LayoutOptions o = Flow.Options();
        o.Password = password;
        return o;
    }

    private static byte[] Docx() => EncryptedFixtures.Open("sniff-pw.docx").ToArray();

    private static byte[] Xlsx() => EncryptedFixtures.Open("sample-pw.xlsx").ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_document_without_a_password(string? password)
    {
        LayoutPasswordRequiredException ex = Assert.Throws<LayoutPasswordRequiredException>(() => DocsLayout.Open(Docx(), null, With(password)));
        Assert.False(ex.PasswordGiven);
        Assert.StartsWith("The document is encrypted: set LayoutOptions.Password. ", ex.Message, StringComparison.Ordinal);
        Assert.IsType<DocxPasswordRequiredException>(ex.InnerException);
    }

    [Fact]
    public void A_document_with_a_wrong_password()
    {
        LayoutPasswordRequiredException ex = Assert.Throws<LayoutPasswordRequiredException>(() => DocsLayout.Open(Docx(), null, With("nope")));
        Assert.True(ex.PasswordGiven);
        Assert.StartsWith("The password for this document is wrong: ", ex.Message, StringComparison.Ordinal);
        Assert.IsType<DocxPasswordRequiredException>(ex.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_workbook_without_a_password(string? password)
    {
        LayoutPasswordRequiredException ex = Assert.Throws<LayoutPasswordRequiredException>(() => SheetsLayout.Open(Xlsx(), null, With(password)));
        Assert.False(ex.PasswordGiven);
        Assert.StartsWith("The workbook is encrypted: set LayoutOptions.Password. ", ex.Message, StringComparison.Ordinal);
        Assert.IsType<XlsxPasswordRequiredException>(ex.InnerException);
    }

    [Fact]
    public void A_workbook_with_a_wrong_password()
    {
        LayoutPasswordRequiredException ex = Assert.Throws<LayoutPasswordRequiredException>(() => SheetsLayout.Open(Xlsx(), null, With("nope")));
        Assert.True(ex.PasswordGiven);
        Assert.StartsWith("The password for this workbook is wrong: ", ex.Message, StringComparison.Ordinal);
        Assert.IsType<XlsxPasswordRequiredException>(ex.InnerException);
    }

    [Fact]
    public void AutoDetect_reports_a_missing_or_wrong_password_for_both_kinds()
    {
        foreach (byte[] file in new[] { Docx(), Xlsx() })
        {
            // Without a password the content is unknown; Chuvadi.Docs is asked first and reports it.
            LayoutPasswordRequiredException none = Assert.Throws<LayoutPasswordRequiredException>(() => AutoDetectLayout.Open(file, LayoutSourceFormat.Auto, With(null)));
            Assert.False(none.PasswordGiven);
            LayoutPasswordRequiredException wrong = Assert.Throws<LayoutPasswordRequiredException>(() => AutoDetectLayout.Open(new MemoryStream(file), LayoutSourceFormat.Auto, With("nope")));
            Assert.True(wrong.PasswordGiven);
        }

        // The right password still opens each as what it is.
        using LaidOutDocument doc = AutoDetectLayout.Open(Docx(), LayoutSourceFormat.Auto, With("pw"));
        Assert.Equal(LayoutSourceFormat.Docx, doc.Source.Format);
        using LaidOutDocument book = AutoDetectLayout.Open(Xlsx(), LayoutSourceFormat.Auto, With("pw"));
        Assert.Equal(LayoutSourceFormat.Xlsx, book.Source.Format);
    }

    [Fact]
    public void Existing_handlers_still_catch_it()
    {
        LayoutFormatException ex = Assert.ThrowsAny<LayoutFormatException>(() => AutoDetectLayout.Open(Xlsx(), LayoutSourceFormat.Auto, With(null)));
        Assert.IsType<LayoutPasswordRequiredException>(ex);
        Assert.IsAssignableFrom<LayoutException>(ex);
    }

    [Fact]
    public void A_damaged_compound_file_still_asks_for_the_password()
    {
        // A compound file whose directory cannot be read may be a damaged encrypted file, so the shared decryptor
        // (D-293) still treats it as encrypted: without a password, the password exception, as for any encrypted
        // file. With a password the container is parsed and found damaged, which Chuvadi.Docs (2.1.0) and
        // Chuvadi.Sheets report as a format error, not a password error (D-300, D-305).
        byte[] ole = new byte[512];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(ole, 0);
        Assert.False(Assert.Throws<LayoutPasswordRequiredException>(() => AutoDetectLayout.Open(ole, LayoutSourceFormat.Auto, With(null))).PasswordGiven);
        LayoutFormatException withPassword = Assert.Throws<LayoutFormatException>(() => AutoDetectLayout.Open(ole, LayoutSourceFormat.Auto, With("pw")));
        Assert.StartsWith("The file could not be read: ", withPassword.Message);
    }

    private static byte[] Cfb(params (string Name, int Size)[] streams)
    {
        List<Chuvadi.Internal.Crypto.CfbContainer.Node> nodes = new();
        foreach ((string name, int size) in streams)
        {
            byte[] data = new byte[size];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i * 31 + 7);
            }
            nodes.Add(new Chuvadi.Internal.Crypto.CfbContainer.Node { Name = name, Type = Chuvadi.Internal.Crypto.CfbContainer.TypeStream, Data = data });
        }
        MemoryStream ms = new();
        Chuvadi.Internal.Crypto.CfbContainer.Write(ms, nodes);
        return ms.ToArray();
    }

    /// <summary>A Word 97-2003 file's streams, as D-293's own tests build them.</summary>
    private static byte[] LegacyDoc() => Cfb(("WordDocument", 6000), ("1Table", 2000), ("\u0005SummaryInformation", 300));

    /// <summary>An Excel 97-2003 file's streams.</summary>
    private static byte[] LegacyXls() => Cfb(("Workbook", 6000), ("\u0005SummaryInformation", 300));

    [Theory]
    [InlineData(null)]
    [InlineData("pw")]
    public void A_legacy_doc_is_a_format_error_naming_doc_never_a_password_prompt(string? password)
    {
        // Assert.Throws checks the exact type, so these are never LayoutPasswordRequiredException.
        LayoutFormatException docs = Assert.Throws<LayoutFormatException>(() => DocsLayout.Open(LegacyDoc(), null, With(password)));
        Assert.StartsWith("The document could not be read: ", docs.Message, StringComparison.Ordinal);
        Assert.Contains("(.doc)", docs.Message, StringComparison.Ordinal);
        Assert.IsType<DocxFormatException>(docs.InnerException);

        LayoutFormatException auto = Assert.Throws<LayoutFormatException>(() => AutoDetectLayout.Open(LegacyDoc(), LayoutSourceFormat.Auto, With(password)));
        Assert.StartsWith("The file could not be read: ", auto.Message, StringComparison.Ordinal);
        Assert.Contains("(.doc)", auto.Message, StringComparison.Ordinal);
        Assert.IsType<XlsxFormatException>(auto.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pw")]
    public void A_legacy_xls_is_a_format_error_naming_xls_never_a_password_prompt(string? password)
    {
        LayoutFormatException sheets = Assert.Throws<LayoutFormatException>(() => SheetsLayout.Open(LegacyXls(), null, With(password)));
        Assert.StartsWith("The workbook could not be read: ", sheets.Message, StringComparison.Ordinal);
        Assert.Contains("(.xls)", sheets.Message, StringComparison.Ordinal);
        Assert.IsType<XlsxFormatException>(sheets.InnerException);

        LayoutFormatException auto = Assert.Throws<LayoutFormatException>(() => AutoDetectLayout.Open(new MemoryStream(LegacyXls()), LayoutSourceFormat.Auto, With(password)));
        Assert.StartsWith("The file could not be read: ", auto.Message, StringComparison.Ordinal);
        Assert.Contains("(.xls)", auto.Message, StringComparison.Ordinal);
        Assert.IsType<XlsxFormatException>(auto.InnerException);
    }

    [Fact]
    public void The_engine_without_a_decryptor_still_names_both_possibilities()
    {
        // DocumentLayout cannot look inside an OLE file, so its message keeps both readings (D-293 finding 3).
        LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => DocumentLayout.Open(new MemoryStream(LegacyDoc()), LayoutSourceFormat.Auto, With(null)));
        Assert.Contains("legacy .doc/.xls", ex.Message, StringComparison.Ordinal);
    }
}
