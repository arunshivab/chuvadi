using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Internal.Crypto;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.Tests;

/// <summary>
/// D-253 / D-293: an old binary Office file (a compound file without the EncryptionInfo and EncryptedPackage streams)
/// is not "encrypted". It must not get a password prompt; it is an XlsxFormatException that names the old format.
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

    private static byte[] LegacyExcel() => Cfb(("Workbook", 6000), ("\u0005SummaryInformation", 300));
    private static byte[] LegacyExcel5() => Cfb(("Book", 6000));
    private static byte[] LegacyWord() => Cfb(("WordDocument", 6000), ("1Table", 2000));

    private static byte[] Encrypted(string password)
    {
        var ms = new MemoryStream();
        using (var w = XlsxWriter.Create(ms, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = password, SpinCount = 1_000 } }))
        {
            var s = w.AddSheet("S");
            s.WriteRow(1);
            for (int i = 2; i < 200; i++) s.WriteRow("row " + i);
            w.Save();
        }
        return ms.ToArray();
    }

    private static void AssertLegacy(Action read, string format)
    {
        var ex = Assert.Throws<XlsxFormatException>(read);
        Assert.IsNotType<XlsxPasswordRequiredException>(ex);
        Assert.Contains(format, ex.Message);
        Assert.Contains("not an Office Open XML package", ex.Message);
    }

    [Fact]
    public void LegacyExcel_IsAFormatErrorNamingXls_NotAPasswordPrompt_OnEveryEntryPoint()
    {
        byte[] file = LegacyExcel();
        AssertLegacy(() => Workbook.Load(new MemoryStream(file)), ".xls");
        AssertLegacy(() => Workbook.Load(new MemoryStream(file), "pw", null), ".xls");
        AssertLegacy(() => XlsxReader.Open(new MemoryStream(file)), ".xls");
        AssertLegacy(() => XlsxReader.Open(new MemoryStream(file), "pw", null), ".xls");
    }

    [Fact]
    public void LegacyExcel_FromAPath_IsTheSameFormatError()
    {
        string path = Path.Combine(Path.GetTempPath(), "chuvadi_legacy_" + Guid.NewGuid().ToString("N") + ".xls");
        try
        {
            File.WriteAllBytes(path, LegacyExcel());
            AssertLegacy(() => Workbook.Load(path), ".xls");
            AssertLegacy(() => XlsxReader.Open(path), ".xls");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Excel5Book_AndWordFile_NameTheirOwnFormat()
    {
        AssertLegacy(() => Workbook.Load(new MemoryStream(LegacyExcel5())), ".xls");
        AssertLegacy(() => Workbook.Load(new MemoryStream(LegacyWord())), ".doc");
    }

    [Fact]
    public void AnotherCompoundFile_GetsTheGenericMessage()
    {
        var ex = Assert.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(Cfb(("Contents", 800)))));
        Assert.Contains("OLE compound file", ex.Message);
    }

    [Fact]
    public void AnEncryptedFile_StillOpensWithThePassword_AndTheStreamPositionIsRestored()
    {
        byte[] enc = Encrypted("pw");
        using var ms = new MemoryStream(enc);
        Assert.True(EncryptedPackageReader.IsEncryptedPackage(ms));
        Assert.Equal(0, ms.Position);
        Assert.Equal(1.0, Workbook.Load(new MemoryStream(enc), "pw", null)["S"].Cell("A1").Value);
    }

    [Fact]
    public void EncryptedFiles_StillAskForThePassword()
    {
        byte[] enc = Encrypted("pw");
        Assert.Throws<XlsxPasswordRequiredException>(() => Workbook.Load(new MemoryStream(enc), null, null));
        Assert.Throws<XlsxPasswordRequiredException>(() => Workbook.Load(new MemoryStream(enc), "wrong", null));
        Assert.Throws<XlsxPasswordRequiredException>(() => XlsxReader.Open(new MemoryStream(enc), null, null));
        Assert.Throws<XlsxPasswordRequiredException>(() => XlsxReader.Open(new MemoryStream(enc), "wrong", null));
    }

    [Fact]
    public void ACompoundFileWithOnlyOneEncryptionStream_IsAFormatErrorNamingTheMissingStream()
    {
        var onlyInfo = Assert.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(Cfb(("EncryptionInfo", 300))), "pw", null));
        Assert.Contains("EncryptedPackage stream is missing", onlyInfo.Message);
        var onlyPackage = Assert.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(Cfb(("EncryptedPackage", 300))), "pw", null));
        Assert.Contains("EncryptionInfo stream is missing", onlyPackage.Message);
    }

    [Fact]
    public void ADamagedDirectory_KeepsTheOldAnswer_TheDecryptorReportsIt()
    {
        byte[] sigOnly = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        Assert.True(EncryptedPackageReader.IsEncryptedPackage(new MemoryStream(sigOnly)));
        Assert.Throws<XlsxPasswordRequiredException>(() => Workbook.Load(new MemoryStream(sigOnly)));
        var withPassword = Assert.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(sigOnly), "pw", null));
        Assert.IsNotType<XlsxPasswordRequiredException>(withPassword);
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
