using Chuvadi.Docs.Model;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Tests;
using Xunit;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>
/// The follow-up rules of D-181 for .docx: cell margins when the file states none (F3, the standard's 115 twips), and the
/// empty paragraph that ends a cell after a nested table (F2, no height: Word, measured).
/// </summary>
public class FollowUpDocsTests
{
    private static FlowTable FirstTable(Document doc)
        => Assert.IsType<FlowTable>(DocsImportTests.Import(doc).Sections[0].Blocks.First(b => b is FlowTable));

    [Fact]
    public void Cell_margins_default_to_115_twips_when_nothing_states_them()
    {
        // ECMA-376 Part 1, 17.4.34 and 17.4.11: 115 twips left and right; top and bottom 0.
        Document doc = new Document();
        doc.AddTable(2).AddRow("a", "b");
        Chuvadi.Layout.Model.TableFormat f = FirstTable(doc).Format;
        Assert.Equal(5.75, f.CellMarginLeft, 6);
        Assert.Equal(5.75, f.CellMarginRight, 6);
        Assert.Equal(0, f.CellMarginTop, 6);
        Assert.Equal(0, f.CellMarginBottom, 6);
    }

    [Fact]
    public void Stated_cell_margins_still_win()
    {
        Document doc = new Document();
        DocTable t = doc.AddTable(2);
        t.Format.CellMargins = CellMargins.All(4);
        t.AddRow("a", "b");
        Chuvadi.Layout.Model.TableFormat f = FirstTable(doc).Format;
        Assert.Equal(4, f.CellMarginLeft, 6);
        Assert.Equal(4, f.CellMarginRight, 6);
        Assert.Equal(4, f.CellMarginTop, 6);
    }

    [Fact]
    public void The_empty_paragraph_after_a_nested_table_takes_no_height()
    {
        // The file must end the cell with a paragraph after the nested table (Chuvadi.Docs writes it); Word gives it no
        // height in every mode (A7 D4), so the cell ends with the table.
        Document doc = new Document();
        DocTable t = doc.AddTable(1);
        TableRow r = t.AddRow("outer");
        r.Cell(0).AddTable(1).AddRow("inner");
        FlowTableCell cell = FirstTable(doc).Rows[0].Cells[0];
        Assert.IsType<FlowTable>(cell.Blocks[^1]);
        Assert.Equal(2, cell.Blocks.Count);
    }

    [Fact]
    public void A_paragraph_with_text_after_a_nested_table_keeps_its_line()
    {
        Document doc = new Document();
        DocTable t = doc.AddTable(1);
        TableCell c = t.AddRow("outer").Cell(0);
        c.AddTable(1).AddRow("inner");
        c.Paragraphs.Add(new Paragraph("after the inner table"));
        FlowTableCell cell = FirstTable(doc).Rows[0].Cells[0];
        FlowParagraph last = Assert.IsType<FlowParagraph>(cell.Blocks[^1]);
        Assert.NotEmpty(last.Inlines);
    }
}
