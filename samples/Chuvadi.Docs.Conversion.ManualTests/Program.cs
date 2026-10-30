using System;
using System.IO;

namespace Chuvadi.Docs.Conversion.ManualTests;

/// <summary>
/// Console test harness (same shape as Chuvadi.Docs.ManualTests): each group is a set of
/// Check(label, action) assertions; any failure gives a non-zero exit code. Output files
/// (docx, html, md) are written to the out folder for visual checks.
/// Run all groups: <c>dotnet run</c>. One group: <c>dotnet run -- HtmlImport</c>.
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
            ("Text", TextTests.Run),
            ("MarkdownImport", MarkdownImportTests.Run),
            ("MarkdownExport", MarkdownExportTests.Run),
            ("HtmlImport", HtmlImportTests.Run),
            ("HtmlExport", HtmlExportTests.Run),
            ("RoundTrip", RoundTripTests.Run),
            ("ReviewFixes", ReviewFixesTests.Run),
        };
        var filter = args.Length > 0 ? args[0] : null;
        foreach (var (name, run) in groups)
        {
            if (filter is not null && !string.Equals(name, filter, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"=== {name} ===");
            try { run(outDir); }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  GROUP FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"\nOutput: {outDir}");
        Console.WriteLine($"Total: {_passed} passed, {_failed} failed.");
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
            Console.WriteLine($"  FAIL  {label}: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void AssertEqual<T>(T expected, T actual, string context)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException($"{context}: expected [{expected}], got [{actual}].");
    }

    public static void AssertContains(string haystack, string needle, string context)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
            throw new InvalidOperationException($"{context}: expected to find '{needle}' in: {Short(haystack)}");
    }

    public static void AssertNotContains(string haystack, string needle, string context)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{context}: did not expect '{needle}' in: {Short(haystack)}");
    }

    public static void AssertThrows<T>(Action action, string context) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        catch (Exception ex) { throw new InvalidOperationException($"{context}: expected {typeof(T).Name}, got {ex.GetType().Name}."); }
        throw new InvalidOperationException($"{context}: expected {typeof(T).Name}, nothing was thrown.");
    }

    private static string Short(string s) => s.Length > 600 ? s.Substring(0, 600) + "…" : s;

    /// <summary>Reads a part of a saved docx.</summary>
    public static string ReadPart(string docxPath, string partName)
    {
        using var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
        var e = zip.GetEntry(partName) ?? throw new InvalidOperationException($"Part {partName} not found.");
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    /// <summary>A 2×2 red PNG.</summary>
    public static byte[] Png()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP4z8AARAwQCgAf7gP9i18U1AAAAABJRU5ErkJggg==");
    }
}
