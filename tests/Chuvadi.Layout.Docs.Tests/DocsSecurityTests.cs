using System.Diagnostics;
using Chuvadi.Docs.Model;
using Chuvadi.Layout.Docs;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>D-406, the 0.3.1 security patch, on the Docs side (SEC-03): a small file with very many paragraphs is
/// refused while it is read, before the document model grows.</summary>
public class DocsSecurityTests
{
    [Fact]
    public void Many_paragraphs_are_refused_by_the_element_cap_while_reading()
    {
        Document many = new Document();
        for (int i = 0; i < 300_000; i++)
        {
            many.AddParagraph();
        }
        MemoryStream ms = new MemoryStream();
        many.SaveTo(ms);
        byte[] file = ms.ToArray();

        long before = GC.GetTotalAllocatedBytes(true);
        Stopwatch sw = Stopwatch.StartNew();
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() => DocsLayout.Open(file, null, DocsImportTests.Options()));
        long allocated = GC.GetTotalAllocatedBytes(true) - before;
        Assert.Contains(ex.LimitName, new[] { "DocxElements", "DocxCompressionRatio" });
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        // 0.3.0 built the whole model first (about 0.8 KB per paragraph). Now reading stops at the cap: well under 1 GB
        // allocated in all, whatever the GC keeps.
        Assert.True(allocated < 1024L * 1024 * 1024, $"allocated {allocated:N0} bytes");
    }
}
