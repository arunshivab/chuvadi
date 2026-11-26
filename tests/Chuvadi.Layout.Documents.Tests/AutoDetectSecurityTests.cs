using Chuvadi.Layout.Documents;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Sheets.Tests;
using Chuvadi.Layout.Tests;

namespace Chuvadi.Layout.Documents.Tests;

/// <summary>
/// D-406: AutoDetectLayout gets the 0.3.1 fixes with no settings. It cannot turn conditional formats off, so the
/// hostile workbooks of Audit B must lay out or fail cleanly through it (SEC-08, SEC-S1).
/// </summary>
public class AutoDetectSecurityTests
{
    [Fact]
    public void A_conditional_format_nested_10000_deep_lays_out_with_a_warning()
    {
        using LaidOutDocument doc = AutoDetectLayout.Open(new MemoryStream(CraftedXlsx.NestedConditionalFormat(10_000)), LayoutSourceFormat.Auto, Flow.Options());
        Assert.Equal(1, doc.ResolvePageCount());
        Assert.Contains(doc.Warnings, w => w.Code == LayoutWarningCodes.ConditionalFormatNotEvaluated);
    }

    [Fact]
    public void Shape_groups_nested_20000_deep_are_a_format_error_not_a_crash()
    {
        Assert.Throws<LayoutFormatException>(
            () => AutoDetectLayout.Open(new MemoryStream(CraftedXlsx.NestedShapeGroups(20_000)), LayoutSourceFormat.Auto, Flow.Options()));
        using LaidOutDocument ok = AutoDetectLayout.Open(new MemoryStream(CraftedXlsx.NestedShapeGroups(256)), LayoutSourceFormat.Auto, Flow.Options());
        Assert.Equal(1, ok.ResolvePageCount());
    }
}
