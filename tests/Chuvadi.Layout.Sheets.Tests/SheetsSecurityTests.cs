using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Sheets;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Layout.Sheets.Tests;

/// <summary>D-406, the 0.3.1 security patch, on the Sheets side: the reader limits Layout passes to Chuvadi.Sheets.</summary>
public class SheetsSecurityTests
{
    [Fact]
    public void Safe_limits_read_workbooks_with_the_Sheets_untrusted_preset_and_a_200_to_1_ratio()
    {
        XlsxReaderOptions safe = SheetsImporter.ReaderOptions(new LayoutLimits());
        XlsxReaderOptions preset = XlsxReaderOptions.ForUntrustedInput();
        Assert.Equal(200, safe.MaxCompressionRatio);
        Assert.Equal(preset.MaxPackageEntries, safe.MaxPackageEntries);
        Assert.Equal(preset.MaxRowsPerSheet, safe.MaxRowsPerSheet);
        Assert.Equal(preset.MaxSpinCount, safe.MaxSpinCount);
        Assert.Equal(50L * 1024 * 1024, safe.MaxPartBytes);
        Assert.Equal(256L * 1024 * 1024, safe.MaxTotalUncompressedBytes);
        Assert.Equal(1_000_000, safe.MaxCells);
        Assert.Equal(200, SheetsImporter.ReaderOptions(LayoutLimits.ForUntrustedInput()).MaxCompressionRatio);
    }

    [Fact]
    public void Trusted_limits_read_workbooks_as_0_3_0_did()
    {
        LayoutLimits trusted = LayoutLimits.ForTrustedInput();
        trusted.MaxPages = 3;
        XlsxReaderOptions r = SheetsImporter.ReaderOptions(trusted.Clone());
        Assert.Equal(1000, r.MaxCompressionRatio);
        Assert.Equal(50_000, r.MaxPackageEntries);
        Assert.Equal(1_048_576, r.MaxRowsPerSheet);
        Assert.Equal(256L * 1024 * 1024, r.MaxPartBytes);
        Assert.Equal(512L * 1024 * 1024, r.MaxTotalUncompressedBytes);
        Assert.Equal(5_000_000, r.MaxCells);
    }

    [Fact]
    public void MaxCellsPerSheet_is_Sheets_workbook_wide_MaxCells_and_stops_while_reading()
    {
        // D-413: 30 cells over two sheets of 15 each; a per-sheet check alone would pass at 20.
        Workbook wb = new Workbook();
        for (int s = 0; s < 2; s++)
        {
            Sheet sh = wb.AddSheet("S" + s);
            for (int r = 1; r <= 15; r++)
            {
                sh.Cell("A" + r).Value = r;
            }
        }
        LayoutOptions o = SheetsImportTests.Options();
        o.Limits.MaxCellsPerSheet = 20;
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(
            () => SheetsLayout.Open(new MemoryStream(SheetsImportTests.Save(wb)), null, o));
        Assert.Equal("MaxCells", ex.LimitName);
        o.Limits.MaxCellsPerSheet = 30;
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(SheetsImportTests.Save(wb)), null, o);
        Assert.True(doc.ResolvePageCount() >= 2);
    }

    [Fact]
    public void A_conditional_format_nested_10000_deep_lays_out_with_a_warning_and_does_not_crash()
    {
        // SEC-08: 0.3.0 on Sheets 2.2.0 died with a stack overflow; Sheets 2.2.1 limits formula nesting to 256.
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(CraftedXlsx.NestedConditionalFormat(10_000)), null, SheetsImportTests.Options());
        Assert.Equal(1, doc.ResolvePageCount());
        Assert.Contains(doc.Warnings, w => w.Code == LayoutWarningCodes.ConditionalFormatNotEvaluated);
    }

    [Fact]
    public void A_conditional_format_nested_within_the_limit_is_applied()
    {
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(CraftedXlsx.NestedConditionalFormat(200)), null, SheetsImportTests.Options());
        Assert.Equal(1, doc.ResolvePageCount());
        Assert.DoesNotContain(doc.Warnings, w => w.Code == LayoutWarningCodes.ConditionalFormatNotEvaluated);
    }

    [Fact]
    public void Conditional_formats_honour_the_layout_token()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(
            () => SheetsLayout.Open(new MemoryStream(CraftedXlsx.NestedConditionalFormat(10)), null, SheetsImportTests.Options(), cts.Token));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(20_000)]
    public void Nested_shape_groups_never_crash(int groups)
    {
        // SEC-S1: 20,000 nested groups overflowed the stack in Workbook.Load on Sheets 2.2.0.
        byte[] file = CraftedXlsx.NestedShapeGroups(groups);
        if (groups <= 256)
        {
            using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(file), null, SheetsImportTests.Options());
            Assert.Equal(1, doc.ResolvePageCount());
            Assert.NotNull(doc.GetPage(0));
        }
        else
        {
            Assert.Throws<LayoutFormatException>(() => SheetsLayout.Open(new MemoryStream(file), null, SheetsImportTests.Options()));
        }
    }
}
