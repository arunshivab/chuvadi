using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>Reads a reference PDF's text (with glyph positions) through chuvadi-pdf.</summary>
    internal static class ReferencePdf
    {
        public static DocumentText Read(byte[] pdf)
        {
            using MemoryStream ms = new MemoryStream(pdf, writable: false);
            using PdfDocument doc = PdfDocument.Open(ms);
            List<PageText> pages = new List<PageText>();
            for (int i = 0; i < doc.Pages.Count; i++)
            {
                PdfPage page = doc.Pages[i];
                double width = page.Width;
                double height = page.Height;
                double top = Math.Max(page.MediaBox.Y1, page.MediaBox.Y2);
                List<TextGlyph> glyphs = new List<TextGlyph>();
                foreach (TextRun run in doc.GetTextRuns(i))
                {
                    if (run.Direction == TextDirection.TopToBottom)
                    {
                        continue;
                    }
                    foreach (GlyphPosition g in run.Glyphs)
                    {
                        // PDF user space has y up from the bottom of the media box; turn it top-down.
                        glyphs.Add(new TextGlyph(g.X - Math.Min(page.MediaBox.X1, page.MediaBox.X2), top - g.Y, g.Advance, g.Unicode, run.FontSize));
                    }
                }
                pages.Add(new PageText(i, width, height, LineGrouper.Group(glyphs)));
            }
            return new DocumentText(pages);
        }
    }
}
