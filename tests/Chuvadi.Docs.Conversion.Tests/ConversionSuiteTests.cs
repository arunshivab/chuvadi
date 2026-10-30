using System.IO;
using Chuvadi.Docs.Conversion.ManualTests;

namespace Chuvadi.Docs.Conversion.Tests;

/// <summary>CI-facing suite: one fact per manual verification group, so failures localize.</summary>
public class ConversionSuiteTests
{
    private static string FreshOutDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chuvadi-docs-conversion-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact] public void Suite_Text() => TextTests.Run(FreshOutDir());
    [Fact] public void Suite_MarkdownImport() => MarkdownImportTests.Run(FreshOutDir());
    [Fact] public void Suite_MarkdownExport() => MarkdownExportTests.Run(FreshOutDir());
    [Fact] public void Suite_HtmlImport() => HtmlImportTests.Run(FreshOutDir());
    [Fact] public void Suite_HtmlExport() => HtmlExportTests.Run(FreshOutDir());
    [Fact] public void Suite_RoundTrip() => RoundTripTests.Run(FreshOutDir());
    [Fact] public void Suite_ReviewFixes() => ReviewFixesTests.Run(FreshOutDir());
}
