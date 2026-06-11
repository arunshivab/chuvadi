using System;
using System.IO;
using Chuvadi.Sheets.Excel;
using Chuvadi.Sheets.Zip;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Hostile-input hardening + streaming-encryption verification:
///   - XlsxWriter streaming export with workbook encryption (no in-memory plaintext)
///   - ZipExtractionLimits (per-entry, total, and entry-count caps)
///   - XlsxReaderOptions.MaxPartBytes (decompression-bomb guard)
/// </summary>
internal static class HardeningTest
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[HardeningTest] Running hardening + streaming-encryption tests...");
        Console.WriteLine();

        int pass = 0;
        int fail = 0;

        void Check(string label, Action body)
        {
            try
            {
                body();
                Console.WriteLine($"    PASS: {label}");
                pass++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: {label}");
                Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}");
                fail++;
            }
        }

        // ---- 1. Streaming writer + encryption ------------------------------------
        Check("XlsxWriter — streaming export with Encryption option round-trips", () =>
        {
            var path = Path.Combine(outputDir, "streamed_encrypted.xlsx");
            if (File.Exists(path)) File.Delete(path);

            using (var writer = XlsxWriter.Create(path, new XlsxWriterOptions
            {
                Encryption = new EncryptionOptions { Password = "stream-pass" },
            }))
            {
                using var sheet = writer.AddSheet("Big");
                sheet.SetHeaderFooter("&CStreamed + Encrypted", "&RPage &P");
                sheet.WriteHeader("Id", "Value");
                for (int i = 1; i <= 10_000; i++)
                {
                    int row = i;
                    sheet.WriteRow(r => r.Cell(row).Cell($"value-{row}"));
                }
                writer.Save();
            }

            // Output must be a CFB container (encrypted), not a raw zip.
            Span<byte> sig = stackalloc byte[4];
            using (var fs = File.OpenRead(path)) fs.ReadExactly(sig);
            if (!(sig[0] == 0xD0 && sig[1] == 0xCF && sig[2] == 0x11 && sig[3] == 0xE0))
                throw new Exception("Output is not CFB-wrapped (not encrypted).");

            // No leftover plaintext package temp files.
            foreach (var leftover in Directory.GetFiles(Path.GetTempPath(), "chuvadi_sheets_pkg_*.tmp"))
                throw new Exception($"Plaintext package temp file not cleaned up: {leftover}");

            var wb = Workbook.Load(path, "stream-pass");
            var s = wb["Big"];
            if (!Equals(s.Cell("A2").Value, 1.0)) throw new Exception($"A2: {s.Cell("A2").Value}");
            if (!Equals(s.Cell("B10001").Value, "value-10000"))
                throw new Exception($"B10001: {s.Cell("B10001").Value}");
        });

        Check("XlsxWriter — Encryption with wrong password still rejected", () =>
        {
            var path = Path.Combine(outputDir, "streamed_encrypted.xlsx");
            try
            {
                _ = Workbook.Load(path, "not-the-password");
                throw new Exception("Wrong password was accepted.");
            }
            catch (XlsxPasswordRequiredException) { /* expected */ }
        });

        // ---- 2. Zip extraction limits ---------------------------------------------
        // A highly compressible 5 MB entry: tiny on disk, big when inflated.
        var bombZip = Path.Combine(outputDir, "limits_bomb.zip");
        if (File.Exists(bombZip)) File.Delete(bombZip);
        using (var zw = ZipWriter.Create(bombZip))
        {
            zw.AddBytes("zeros.bin", new byte[5 * 1024 * 1024]);
            zw.AddText("small.txt", "ok");
        }

        Check("ZipExtractionLimits — MaxEntryBytes stops a decompression bomb", () =>
        {
            var target = Path.Combine(outputDir, "limits_target1");
            using var zr = ZipReader.Open(bombZip);
            try
            {
                zr.ExtractTo(target, new ZipExtractionLimits { MaxEntryBytes = 64 * 1024 });
                throw new Exception("Expected the oversized entry to be rejected.");
            }
            catch (ZipFormatException ex) when (ex.Message.Contains("limit")) { /* expected */ }
        });

        Check("ZipExtractionLimits — MaxTotalBytes caps the running total", () =>
        {
            var target = Path.Combine(outputDir, "limits_target2");
            using var zr = ZipReader.Open(bombZip);
            try
            {
                zr.ExtractTo(target, new ZipExtractionLimits { MaxTotalBytes = 1024 * 1024 });
                throw new Exception("Expected the total budget to be exceeded.");
            }
            catch (ZipFormatException ex) when (ex.Message.Contains("limit")) { /* expected */ }
        });

        Check("ZipExtractionLimits — MaxEntries rejects entry flooding", () =>
        {
            var target = Path.Combine(outputDir, "limits_target3");
            using var zr = ZipReader.Open(bombZip);
            try
            {
                zr.ExtractTo(target, new ZipExtractionLimits { MaxEntries = 1 });
                throw new Exception("Expected the entry count to be rejected.");
            }
            catch (ZipFormatException ex) when (ex.Message.Contains("entries")) { /* expected */ }
        });

        Check("ZipExtractionLimits — generous limits extract normally", () =>
        {
            var target = Path.Combine(outputDir, "limits_target_ok");
            using var zr = ZipReader.Open(bombZip);
            zr.ExtractTo(target, new ZipExtractionLimits
            {
                MaxEntries = 100,
                MaxEntryBytes = 64 * 1024 * 1024,
                MaxTotalBytes = 128 * 1024 * 1024,
            });
            var extracted = new FileInfo(Path.Combine(target, "zeros.bin"));
            if (extracted.Length != 5 * 1024 * 1024) throw new Exception($"Size: {extracted.Length}");
        });

        // ---- 3. XlsxReader part-size cap -------------------------------------------
        // Build an xlsx whose sheet XML decompresses well past a tiny cap.
        var bigXlsx = Path.Combine(outputDir, "limits_big.xlsx");
        if (File.Exists(bigXlsx)) File.Delete(bigXlsx);
        using (var writer = XlsxWriter.Create(bigXlsx))
        {
            using var sheet = writer.AddSheet("Data");
            sheet.WriteHeader("Text");
            for (int i = 0; i < 2_000; i++)
                sheet.WriteRow(r => r.Cell(new string('x', 100)));
            writer.Save();
        }

        Check("XlsxReaderOptions.MaxPartBytes — tiny cap rejects oversized sheet XML", () =>
        {
            try
            {
                using var reader = XlsxReader.Open(bigXlsx, new XlsxReaderOptions
                {
                    MaxPartBytes = 16 * 1024,
                    TreatFirstRowAsHeaders = false,
                });
                int rows = 0;
                foreach (var _ in reader.Sheet(1).Rows) rows++;
                throw new Exception($"Expected the part-size cap to trigger; read {rows} rows.");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("limit")) { /* expected */ }
        });

        Check("XlsxReaderOptions.MaxPartBytes — generous cap reads normally", () =>
        {
            using var reader = XlsxReader.Open(bigXlsx, new XlsxReaderOptions
            {
                MaxPartBytes = 256 * 1024 * 1024,
                TreatFirstRowAsHeaders = false,
            });
            int rows = 0;
            foreach (var _ in reader.Sheet(1).Rows) rows++;
            if (rows != 2_001) throw new Exception($"Rows: {rows}");
        });

        Console.WriteLine();
        Console.WriteLine($"[HardeningTest] Done. {pass} passed, {fail} failed.");
        if (fail > 0) throw new Exception($"{fail} hardening tests failed.");
    }
}
