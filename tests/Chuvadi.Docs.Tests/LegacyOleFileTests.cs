using System.IO;
using Chuvadi.Docs.Model;
using Chuvadi.Internal.Crypto;

namespace Chuvadi.Docs.Tests;

/// <summary>
/// D-253 / D-293: an old binary Office file (a compound file without the EncryptionInfo and EncryptedPackage streams)
/// is not "encrypted". It must not get a password prompt; it is a DocxFormatException that names the old format.
/// The compound files are built with the shared CfbContainer writer, so no binary fixture is needed.
/// </summary>
public sealed class LegacyOleFileTests
{
    private static byte[] Cfb(params (string Name, int Size)[] streams)
    {
        var nodes = new List<CfbContainer.Node>();
        foreach (var (name, size) in streams)
        {
            var data = new byte[size];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 7);
            nodes.Add(new CfbContainer.Node { Name = name, Type = CfbContainer.TypeStream, Data = data });
        }
        var ms = new MemoryStream();
        CfbContainer.Write(ms, nodes);
        return ms.ToArray();
    }

    private static byte[] LegacyWord() => Cfb(("WordDocument", 6000), ("1Table", 2000), ("\u0005SummaryInformation", 300));
    private static byte[] LegacyExcel() => Cfb(("Workbook", 6000), ("\u0005SummaryInformation", 300));
    private static byte[] LegacyPowerPoint() => Cfb(("PowerPoint Document", 6000), ("Current User", 100));

    private static byte[] Encrypted(string password)
    {
        var doc = new Document();
        doc.AddParagraph("secret text");
        var ms = new MemoryStream();
        doc.SaveTo(ms, new EncryptionOptions { Password = password, SpinCount = 1_000 });
        return ms.ToArray();
    }

    // DocxReader.Open wraps every failure in a fixed message ("Could not open the file as a docx package."), so for
    // it the text that names the old format is on the InnerException only (outerNamesIt: false).
    private static void AssertLegacy(Action read, string format, bool outerNamesIt = true)
    {
        var ex = Assert.Throws<DocxFormatException>(read);
        Assert.IsNotType<DocxPasswordRequiredException>(ex);
        var inner = Assert.IsType<InvalidDataException>(ex.InnerException);
        Assert.Contains(format, inner.Message);
        Assert.Contains("not an Office Open XML package", inner.Message);
        if (outerNamesIt) Assert.Contains(format, ex.Message);
    }

    [Fact]
    public void LegacyWord_IsAFormatErrorNamingDoc_NotAPasswordPrompt_OnEveryEntryPoint()
    {
        byte[] file = LegacyWord();
        AssertLegacy(() => Document.Load(new MemoryStream(file)), ".doc");
        AssertLegacy(() => Document.Load(new MemoryStream(file), "pw"), ".doc");
        AssertLegacy(() => DocxReader.Open(new MemoryStream(file)), ".doc", outerNamesIt: false);
        AssertLegacy(() => DocxReader.Open(new MemoryStream(file), "pw"), ".doc", outerNamesIt: false);
        AssertLegacy(() => DocxTemplate.CheckTemplate(new MemoryStream(file)), ".doc");
        AssertLegacy(() => DocxTemplate.GetPlaceholders(new MemoryStream(file), "pw"), ".doc");
    }

    [Fact]
    public void LegacyWord_FromAPath_IsTheSameFormatError()
    {
        string path = Path.Combine(Path.GetTempPath(), "chuvadi_legacy_" + Guid.NewGuid().ToString("N") + ".doc");
        try
        {
            File.WriteAllBytes(path, LegacyWord());
            AssertLegacy(() => Document.Load(path), ".doc");
            AssertLegacy(() => DocxReader.Open(path), ".doc", outerNamesIt: false);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyExcelAndPowerPoint_NameTheirOwnFormat()
    {
        AssertLegacy(() => Document.Load(new MemoryStream(LegacyExcel())), ".xls");
        AssertLegacy(() => Document.Load(new MemoryStream(LegacyPowerPoint())), ".ppt");
    }

    [Fact]
    public void AnotherCompoundFile_GetsTheGenericMessage()
    {
        var ex = Assert.Throws<DocxFormatException>(() => Document.Load(new MemoryStream(Cfb(("Contents", 800)))));
        Assert.Contains("OLE compound file", ex.Message);
    }

    [Fact]
    public void ReadingDoesNotMoveTheCallersStreamBeyondTheFile_AndAnEncryptedFileStillOpens()
    {
        byte[] enc = Encrypted("pw");
        using var ms = new MemoryStream(enc);
        Assert.True(EncryptedPackageReader.IsEncryptedPackage(ms));
        Assert.Equal(0, ms.Position);
        Assert.Contains("secret text", Document.Load(new MemoryStream(enc), "pw").GetText());
    }

    [Fact]
    public void EncryptedFiles_StillAskForThePassword()
    {
        byte[] enc = Encrypted("pw");
        Assert.Throws<DocxPasswordRequiredException>(() => Document.Load(new MemoryStream(enc)));
        Assert.Throws<DocxPasswordRequiredException>(() => Document.Load(new MemoryStream(enc), "wrong"));
        Assert.Throws<DocxPasswordRequiredException>(() => DocxReader.Open(new MemoryStream(enc)));
        Assert.Throws<DocxPasswordRequiredException>(() => DocxReader.Open(new MemoryStream(enc), "wrong"));
    }

    [Fact]
    public void ACompoundFileWithOnlyOneEncryptionStream_IsAFormatErrorNamingTheMissingStream()
    {
        var onlyInfo = Assert.Throws<DocxFormatException>(() => Document.Load(new MemoryStream(Cfb(("EncryptionInfo", 300))), "pw"));
        Assert.Contains("EncryptedPackage stream is missing", onlyInfo.Message);
        var onlyPackage = Assert.Throws<DocxFormatException>(() => Document.Load(new MemoryStream(Cfb(("EncryptedPackage", 300))), "pw"));
        Assert.Contains("EncryptionInfo stream is missing", onlyPackage.Message);
    }

    [Fact]
    public void ADamagedDirectory_KeepsTheOldAnswer_TheDecryptorReportsIt()
    {
        // The 8-byte signature and nothing else: the directory cannot be read, so it is still treated as encrypted.
        byte[] sigOnly = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        Assert.True(EncryptedPackageReader.IsEncryptedPackage(new MemoryStream(sigOnly)));
        Assert.Throws<DocxPasswordRequiredException>(() => Document.Load(new MemoryStream(sigOnly)));
        // With a password Docs reports the damaged container its own way (a password error, as in 2.0); what matters
        // here is that it is never told it is an old binary file.
        var withPassword = Assert.ThrowsAny<Exception>(() => Document.Load(new MemoryStream(sigOnly), "pw"));
        Assert.DoesNotContain("binary Office file", withPassword.Message);
    }

    [Fact]
    public void NonCompoundInput_IsNotEncrypted()
    {
        Assert.False(EncryptedPackageReader.IsEncryptedPackage(new MemoryStream(new byte[] { 0x50, 0x4B, 3, 4, 0, 0, 0, 0, 0 })));
        Assert.False(EncryptedPackageReader.IsEncryptedPackage(new MemoryStream(new byte[3])));
    }
    /// <summary>Counts Read calls: the caller's stream may be slow, and the encrypted-file check has no cancellation.</summary>
    private sealed class CountingStream : MemoryStream
    {
        public int Reads;
        public CountingStream(byte[] b) : base(b) { }
        public override int Read(byte[] buffer, int offset, int count) { Reads++; return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Reads++; return base.Read(buffer); }
    }

    [Fact]
    public void EncryptedFileCheck_NeedsOnlyAHandfulOfReads_WhateverTheFileSize()
    {
        // A small file, then a file of about 9 MB whose directory sits beyond the header's 109 FAT entries,
        // which makes the check follow the DIFAT. Neither may read the whole FAT.
        foreach (int size in new[] { 6000, 9_000_000 })
        {
            byte[] file = Cfb(("Workbook", size), ("\u0005SummaryInformation", 300));
            using var s = new CountingStream(file);
            var ex = Assert.Throws<InvalidDataException>(() => EncryptedPackageReader.IsEncryptedPackage(s));
            Assert.Contains(".xls", ex.Message);
            Assert.True(s.Reads <= 12, "reads: " + s.Reads + " for " + size + " bytes");
            Assert.Equal(0, s.Position);
        }
    }
}
