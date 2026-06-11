using System;
using System.IO;
using Chuvadi.Sheets.Excel;
using Chuvadi.Sheets.ManualTests;

namespace Chuvadi.Sheets.Tests;

/// <summary>
/// xUnit entry points for the verification suite. Each fact runs one test group from the
/// ManualTests assembly into an isolated temp directory; groups throw on any failed check,
/// which xUnit reports as a test failure. This gives CI granular pass/fail per area while
/// keeping a single source of truth for the checks themselves.
/// </summary>
public sealed class VerificationSuiteTests : IDisposable
{
    private readonly string _dir;

    public VerificationSuiteTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"chuvadi_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void MinimalXlsx() => MinimalXlsxTest.Run(_dir);

    [Fact]
    public void StyledXlsx() => StyledXlsxTest.Run(_dir);

    [Fact]
    public void StreamingWriter() => StreamingWriterTest.Run(_dir);

    [Fact]
    public void FeaturesPolish() => FeaturesPolishTest.Run(_dir);

    [Fact]
    public void ErgonomicExport() => ErgonomicExportTest.Run(_dir);

    [Fact]
    public void Reader()
    {
        // The reader group re-reads files produced by the earlier groups, so produce them first.
        MinimalXlsxTest.Run(_dir);
        StyledXlsxTest.Run(_dir);
        StreamingWriterTest.Run(_dir);
        ErgonomicExportTest.Run(_dir);
        ReaderTest.Run(_dir);
    }

    [Fact]
    public void Zip() => ZipExtensionTest.Run(_dir);

    [Fact]
    public void ProtectionAndEncryption() => ProtectionTest.Run(_dir);

    [Fact]
    public void Hardening() => HardeningTest.Run(_dir);
}

/// <summary>Targeted unit tests for behaviors worth asserting independently of the groups.</summary>
public sealed class FocusedTests
{
    [Fact]
    public void EncryptedWorkbook_WrongPassword_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chuvadi_{Guid.NewGuid():N}.xlsx");
        try
        {
            var wb = new Workbook();
            wb.AddSheet("S").Cell("A1").Value = 1;
            wb.SaveTo(path, new EncryptionOptions { Password = "right" });

            Assert.Throws<XlsxPasswordRequiredException>(() => Workbook.Load(path, "wrong"));
            Assert.Throws<XlsxPasswordRequiredException>(() => Workbook.Load(path, password: null));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void TamperedCiphertext_FailsIntegrityCheck()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("integrity payload 0123456789");
        using var enc = new MemoryStream();
        var p = Chuvadi.Internal.Crypto.AgileEncryption.Encrypt(payload, "pw", enc);

        var bytes = enc.ToArray();
        bytes[bytes.Length / 2] ^= 0xFF;

        using var tampered = new MemoryStream(bytes);
        var ex = Assert.Throws<InvalidDataException>(
            () => Chuvadi.Internal.Crypto.AgileEncryption.Decrypt(tampered, "pw", p));
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StreamAndArrayEncryption_ProduceEquivalentRoundTrips()
    {
        var payload = new byte[10_000];
        new Random(42).NextBytes(payload);

        using var encA = new MemoryStream();
        var pa = Chuvadi.Internal.Crypto.AgileEncryption.Encrypt(payload, "pw", encA);
        using var inA = new MemoryStream(encA.ToArray());
        var outA = Chuvadi.Internal.Crypto.AgileEncryption.Decrypt(inA, "pw", pa);
        Assert.Equal(payload, outA);

        using var src = new MemoryStream(payload);
        using var encB = new MemoryStream();
        var pb = Chuvadi.Internal.Crypto.AgileEncryption.Encrypt(src, payload.Length, "pw", encB);
        using var inB = new MemoryStream(encB.ToArray());
        var outB = Chuvadi.Internal.Crypto.AgileEncryption.Decrypt(inB, "pw", pb);
        Assert.Equal(payload, outB);
    }

    [Fact]
    public void HeaderFooter_RoundTripsThroughModelApi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chuvadi_{Guid.NewGuid():N}.xlsx");
        try
        {
            var wb = new Workbook();
            var s = wb.AddSheet("HF");
            s.Cell("A1").Value = "x";
            s.SetHeaderFooter("&CTitle", "&RPage &P");
            wb.SaveTo(path);

            using var zip = Chuvadi.Sheets.Zip.ZipReader.Open(path);
            var entry = zip.FindEntry("xl/worksheets/sheet1.xml");
            Assert.NotNull(entry);
            using var es = entry!.OpenRead();
            using var sr = new StreamReader(es);
            var xml = sr.ReadToEnd();
            Assert.Contains("<oddHeader>&amp;CTitle</oddHeader>", xml);
            Assert.Contains("<oddFooter>&amp;RPage &amp;P</oddFooter>", xml);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
