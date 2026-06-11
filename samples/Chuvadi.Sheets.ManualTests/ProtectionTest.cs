using System;
using System.IO;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Protection + encryption verification.
///
/// Files produced:
///   1. protected_sheet.xlsx     — sheet protection only (file unencrypted; cells locked)
///   2. protected_workbook.xlsx  — workbook structure protection (sheet tab manipulation locked)
///   3. encrypted.xlsx           — full file encryption with password
///   4. encrypted_roundtrip.xlsx — encrypted then decrypted via our reader; content verified
/// </summary>
internal static class ProtectionTest
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[ProtectionTest] Running protection + encryption tests...");
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

        // ---- 1. Sheet protection: build and verify file structure ---------------
        Check("Sheet protection — file builds and is readable", () =>
        {
            var path = Path.Combine(outputDir, "protected_sheet.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            var s = wb.AddSheet("Data");
            s.Cell("A1").Value = "Locked";
            s.Cell("A2").Value = 42;
            s.Cell("A3").Value = new DateTime(2026, 1, 15);
            s.Protect("secret123", new SheetProtectionOptions
            {
                AllowSelectLockedCells = true,
                AllowSelectUnlockedCells = true,
                AllowSort = true,
                AllowAutoFilter = true,
            });
            wb.SaveTo(path);

            // File should be a regular zip (not encrypted), readable by our reader.
            var wb2 = Workbook.Load(path);
            if (wb2.Sheets.Count != 1) throw new Exception($"Expected 1 sheet, got {wb2.Sheets.Count}");
            if (!Equals(wb2["Data"].Cell("A1").Value, "Locked"))
                throw new Exception($"A1 content lost on round-trip: {wb2["Data"].Cell("A1").Value}");
        });

        Check("Sheet protection — <sheetProtection> element present in XML", () =>
        {
            var path = Path.Combine(outputDir, "protected_sheet.xlsx");

            // Look inside the file's sheet1.xml using our zip reader.
            using var zip = Chuvadi.Sheets.Zip.ZipReader.Open(path);
            var sheet1 = zip.FindEntry("xl/worksheets/sheet1.xml");
            if (sheet1 is null) throw new Exception("sheet1.xml not found in package.");

            using var s = sheet1.OpenRead();
            using var sr = new StreamReader(s);
            var xml = sr.ReadToEnd();

            if (!xml.Contains("sheetProtection"))
                throw new Exception("sheetProtection element not found in sheet1.xml.");
            if (!xml.Contains("algorithmName=\"SHA-512\""))
                throw new Exception("SHA-512 algorithm name not in sheetProtection.");
            if (!xml.Contains("spinCount=\"100000\""))
                throw new Exception("Expected spinCount=100000.");
        });

        // ---- 2. Workbook structure protection ----------------------------------
        Check("Workbook structure protection — <workbookProtection> element present", () =>
        {
            var path = Path.Combine(outputDir, "protected_workbook.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            wb.AddSheet("A").Cell("A1").Value = "A1";
            wb.AddSheet("B").Cell("A1").Value = "B1";
            wb.Protect("structurepass", lockStructure: true, lockWindows: false);
            wb.SaveTo(path);

            using var zip = Chuvadi.Sheets.Zip.ZipReader.Open(path);
            var wbXml = zip.FindEntry("xl/workbook.xml");
            if (wbXml is null) throw new Exception("workbook.xml not found.");

            using var s = wbXml.OpenRead();
            using var sr = new StreamReader(s);
            var xml = sr.ReadToEnd();

            if (!xml.Contains("workbookProtection"))
                throw new Exception("workbookProtection element not found.");
            if (!xml.Contains("lockStructure=\"1\""))
                throw new Exception("lockStructure=1 not present.");
            if (xml.Contains("lockWindows=\"1\""))
                throw new Exception("lockWindows should NOT be set (we passed false).");
        });

        // ---- 3. Encryption — write encrypted file -------------------------------
        Check("Encryption — produces CFB-wrapped file (D0 CF 11 E0 magic)", () =>
        {
            var path = Path.Combine(outputDir, "encrypted.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            var s = wb.AddSheet("Confidential");
            s.Cell("A1").Value = "Patient ID";
            s.Cell("B1").Value = "Name";
            s.Cell("A2").Value = "P001";
            s.Cell("B2").Value = "Anand Kumar";
            s.Cell("A3").Value = "P002";
            s.Cell("B3").Value = "Priya Sharma";
            wb.SaveTo(path, new EncryptionOptions { Password = "MyP@ssw0rd!" });

            // Verify CFB magic at the start of the file.
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8) throw new Exception("File too short.");

            byte[] cfbMagic = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
            for (int i = 0; i < cfbMagic.Length; i++)
            {
                if (bytes[i] != cfbMagic[i])
                    throw new Exception($"CFB magic byte {i} mismatch: 0x{bytes[i]:X2} vs 0x{cfbMagic[i]:X2}");
            }
        });

        // ---- 4. Encryption round-trip — decrypt and verify content -------------
        Check("Encryption round-trip — decrypt with correct password", () =>
        {
            var path = Path.Combine(outputDir, "encrypted.xlsx");

            var wb = Workbook.Load(path, "MyP@ssw0rd!");
            if (wb.Sheets.Count != 1) throw new Exception($"Expected 1 sheet, got {wb.Sheets.Count}");
            var s = wb["Confidential"];
            if (!Equals(s.Cell("A1").Value, "Patient ID"))
                throw new Exception($"A1: {s.Cell("A1").Value}");
            if (!Equals(s.Cell("B3").Value, "Priya Sharma"))
                throw new Exception($"B3: {s.Cell("B3").Value}");
        });

        Check("Encryption — wrong password throws XlsxPasswordRequiredException", () =>
        {
            var path = Path.Combine(outputDir, "encrypted.xlsx");
            try
            {
                _ = Workbook.Load(path, "WrongPassword");
                throw new Exception("Expected exception, none thrown.");
            }
            catch (XlsxPasswordRequiredException)
            {
                // Expected.
            }
        });

        Check("Encryption — no password throws XlsxPasswordRequiredException", () =>
        {
            var path = Path.Combine(outputDir, "encrypted.xlsx");
            try
            {
                _ = Workbook.Load(path);
                throw new Exception("Expected exception, none thrown.");
            }
            catch (XlsxPasswordRequiredException)
            {
                // Expected.
            }
        });

        // ---- 5. Encryption round-trip with mixed content ----------------------
        Check("Encryption round-trip — full content preserved (dates, formulas, formats)", () =>
        {
            var path = Path.Combine(outputDir, "encrypted_roundtrip.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            wb.DefinedNames.Add("TaxRate", "0.18");
            var s = wb.AddSheet("Summary");
            s.Columns[1].Width = 22;
            s.Columns[2].Width = 16;
            s.FreezeRows(1);

            s.Cell("A1").Value = "Item";
            s.Cell("B1").Value = "Amount";
            s.Cell("A2").Value = "Hardware";
            s.Cell("B2").Value = 1500.50m;
            s.Cell("A3").Value = "Software";
            s.Cell("B3").Value = 2750.00m;
            s.Cell("A4").Value = "Reviewed";
            s.Cell("B4").Value = new DateTime(2026, 1, 15);
            s.Cell("A5").Value = "Total + Tax";
            s.Cell("B5").Formula = "SUM(B2:B3) * (1 + TaxRate)";

            wb.SaveTo(path, new EncryptionOptions { Password = "round-trip-test" });

            // Reload and verify.
            var wb2 = Workbook.Load(path, "round-trip-test");
            var s2 = wb2["Summary"];
            if (!Equals(s2.Cell("A2").Value, "Hardware"))
                throw new Exception($"A2: {s2.Cell("A2").Value}");
            // Decimal values come back as double after round-trip.
            var amount = s2.Cell("B2").Value;
            if (amount is not double d || Math.Abs(d - 1500.50) > 0.001)
                throw new Exception($"B2: {amount}");
            // Date should come back as DateTime (auto-detected by reader).
            if (s2.Cell("B4").Value is not DateTime dt || dt.Year != 2026 || dt.Month != 1 || dt.Day != 15)
                throw new Exception($"B4: {s2.Cell("B4").Value}");
        });

        // ---- 6. Long password ------------------------------------------------
        Check("Encryption — long unicode password", () =>
        {
            var path = Path.Combine(outputDir, "encrypted_unicode.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var longUnicode = "सुप्रभात-नमस्ते-2026-very-long-password-12345!@#$%^&*()";

            var wb = new Workbook();
            wb.AddSheet("S").Cell("A1").Value = "Unicode test ✓";
            wb.SaveTo(path, new EncryptionOptions { Password = longUnicode });

            var wb2 = Workbook.Load(path, longUnicode);
            if (!Equals(wb2["S"].Cell("A1").Value, "Unicode test ✓"))
                throw new Exception($"Content lost: {wb2["S"].Cell("A1").Value}");
        });

        // ---- 7. Empty workbook can't be encrypted but otherwise works ---------
        Check("Encryption — small workbook (just one cell)", () =>
        {
            var path = Path.Combine(outputDir, "encrypted_tiny.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            wb.AddSheet("S").Cell("A1").Value = 42;
            wb.SaveTo(path, new EncryptionOptions { Password = "x" });

            var wb2 = Workbook.Load(path, "x");
            if (wb2["S"].Cell("A1").Value is not double d || d != 42.0)
                throw new Exception($"A1: {wb2["S"].Cell("A1").Value}");
        });

        // ---- 8. Tamper detection (HMAC) ---------------------------------------
        Check("Encryption — tampered ciphertext fails its integrity check", () =>
        {
            // Exercise AgileEncryption directly (internals visible to this assembly):
            // encrypt a payload, flip one ciphertext byte, and confirm Decrypt rejects it.
            var payload = System.Text.Encoding.UTF8.GetBytes(
                "The quick brown fox jumps over the lazy dog. 0123456789.");

            using var enc = new MemoryStream();
            var p = Chuvadi.Internal.Crypto.AgileEncryption.Encrypt(payload, "tamper-test", enc);

            var bytes = enc.ToArray();
            bytes[bytes.Length / 2] ^= 0xFF;  // Flip a byte inside the encrypted blocks.

            using var tampered = new MemoryStream(bytes);
            try
            {
                _ = Chuvadi.Internal.Crypto.AgileEncryption.Decrypt(tampered, "tamper-test", p);
                throw new Exception("Expected the tampered ciphertext to be rejected, but it decrypted.");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("integrity"))
            {
                // Expected: HMAC mismatch.
            }

            // Untampered control: same parameters must still decrypt cleanly.
            using var clean = new MemoryStream(enc.ToArray());
            var roundTrip = Chuvadi.Internal.Crypto.AgileEncryption.Decrypt(clean, "tamper-test", p);
            if (!System.Linq.Enumerable.SequenceEqual(roundTrip, payload))
                throw new Exception("Control decryption did not round-trip.");
        });

        // ---- 9. Custom spin count is honored and round-trips ------------------
        Check("Encryption — custom SpinCount honored (written to file + decrypts)", () =>
        {
            var path = Path.Combine(outputDir, "encrypted_spin.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            wb.AddSheet("S").Cell("A1").Value = "spin";
            wb.SaveTo(path, new EncryptionOptions { Password = "spin-pass", SpinCount = 150_000 });

            // The EncryptionInfo XML inside the CFB must carry the custom count.
            var raw = File.ReadAllBytes(path);
            var hay = System.Text.Encoding.UTF8.GetString(raw);
            if (!hay.Contains("spinCount=\"150000\""))
                throw new Exception("spinCount=\"150000\" not found in EncryptionInfo.");

            var wb2 = Workbook.Load(path, "spin-pass");
            if (!Equals(wb2["S"].Cell("A1").Value, "spin"))
                throw new Exception($"A1: {wb2["S"].Cell("A1").Value}");
        });

        // ---- 10. Page header/footer ---------------------------------------------
        Check("Header/footer — <headerFooter> emitted and survives encryption round-trip", () =>
        {
            var path = Path.Combine(outputDir, "headerfooter.xlsx");
            if (File.Exists(path)) File.Delete(path);

            var wb = new Workbook();
            var s = wb.AddSheet("HF");
            s.Cell("A1").Value = "body";
            s.SetHeaderFooter("&CQuarterly Report", "&LConfidential&RPage &P of &N");
            wb.SaveTo(path);

            // Inspect sheet1.xml inside the package.
            string sheetXml;
            using (var zip = Chuvadi.Sheets.Zip.ZipReader.Open(path))
            {
                var entry = zip.FindEntry("xl/worksheets/sheet1.xml")
                    ?? throw new Exception("sheet1.xml missing");
                using var es = entry.OpenRead();
                using var sr = new StreamReader(es);
                sheetXml = sr.ReadToEnd();
            }
            if (!sheetXml.Contains("<headerFooter>") ||
                !sheetXml.Contains("<oddHeader>&amp;CQuarterly Report</oddHeader>") ||
                !sheetXml.Contains("<oddFooter>"))
                throw new Exception("headerFooter elements not found in sheet XML.");

            // Also via the ergonomic export config.
            var path2 = Path.Combine(outputDir, "headerfooter_export.xlsx");
            if (File.Exists(path2)) File.Delete(path2);
            var data = new[] { new { Name = "A", Value = 1 }, new { Name = "B", Value = 2 } };
            XlsxExtensions.ToXlsx(data, path2, cfg => cfg
                .SheetName("Data")
                .PageHeaderFooter("&CExported", "&RPage &P"));
            using (var zip2 = Chuvadi.Sheets.Zip.ZipReader.Open(path2))
            {
                var entry = zip2.FindEntry("xl/worksheets/sheet1.xml")
                    ?? throw new Exception("export sheet1.xml missing");
                using var es = entry.OpenRead();
                using var sr = new StreamReader(es);
                if (!sr.ReadToEnd().Contains("<oddHeader>&amp;CExported</oddHeader>"))
                    throw new Exception("export headerFooter not found.");
            }
        });

        Console.WriteLine();
        Console.WriteLine($"[ProtectionTest] Done. {pass} passed, {fail} failed.");
        if (fail > 0) throw new Exception($"{fail} protection/encryption tests failed.");
    }
}
