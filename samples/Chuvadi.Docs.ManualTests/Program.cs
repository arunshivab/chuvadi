using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Console test harness mirroring Chuvadi.Sheets.ManualTests: each group is a set of
/// Check(label, action) assertions; any throw fails the run with a non-zero exit code.
/// Run all groups: <c>dotnet run</c>. One group: <c>dotnet run -- Template</c>.
/// </summary>
public static class Program
{
    private static int _passed;
    private static int _failed;

    public static int Main(string[] args)
    {
        var outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);

        var groups = new (string Name, Action<string> Run)[]
        {
            ("MinimalDocx", MinimalDocxTest.Run),
            ("Formatting", FormattingTest.Run),
            ("Tables", TablesTest.Run),
            ("HeaderFooterPage", HeaderFooterPageTest.Run),
            ("ReaderRoundTrip", ReaderRoundTripTest.Run),
            ("Images", ImageTests.Run),
            ("Template", TemplateTest.Run),
            ("ProtectionEncryption", ProtectionEncryptionTest.Run),
        };

        var filter = args.Length > 0 ? args[0] : null;
        foreach (var (name, run) in groups)
        {
            if (filter is not null && !string.Equals(name, filter, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"=== {name} ===");
            try
            {
                run(outDir);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  GROUP FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nTotal: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    public static void Check(string label, Action assertion)
    {
        try
        {
            assertion();
            _passed++;
            Console.WriteLine($"  PASS  {label}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL  {label}: {ex.Message}");
            throw;
        }
    }

    public static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void AssertContains(string haystack, string needle, string context)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
            throw new InvalidOperationException($"{context}: expected to find '{needle}'.");
    }

    public static void AssertThrows<T>(Action action, string context) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{context}: expected {typeof(T).Name}, got {ex.GetType().Name}.");
        }
        throw new InvalidOperationException($"{context}: expected {typeof(T).Name}, nothing was thrown.");
    }

    /// <summary>Reads a part's XML out of a (plaintext) docx for structural assertions.</summary>
    public static string ReadPartXml(string docxPath, string partName)
    {
        using var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
        var entry = zip.GetEntry(partName)
            ?? throw new InvalidOperationException($"Part '{partName}' not found in {Path.GetFileName(docxPath)}.");
        using var s = entry.Open();
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static List<string> ListParts(string docxPath)
    {
        using var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
        var names = new List<string>();
        foreach (var e in zip.Entries) names.Add(e.FullName);
        return names;
    }
}
