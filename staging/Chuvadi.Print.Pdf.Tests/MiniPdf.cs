using System.Globalization;
using System.Text;

namespace Chuvadi.Print.Pdf.Tests;

/// <summary>
/// Writes small, valid test PDFs by hand (no PDF library), so the adapter is tested against known input:
/// pages of any size and /Rotate with black rectangles at known places, and optional Square annotations
/// with an appearance stream.
/// </summary>
public sealed class MiniPdf
{
    public sealed record Box(double X, double Y, double Width, double Height);

    public sealed record Page(double Width, double Height, int Rotate, Box[] Rectangles, Box? Annotation = null);

    private readonly List<Page> _pages = new();

    public MiniPdf Add(double width, double height, int rotate = 0, Box? annotation = null, params Box[] rectangles)
    {
        _pages.Add(new Page(width, height, rotate, rectangles, annotation));
        return this;
    }

    public byte[] ToArray()
    {
        // Object numbers: 1 catalog, 2 pages, then per page: page, content, [annot, appearance].
        var objects = new List<string>();
        var kids = new List<int>();
        int next = 3;
        var bodies = new SortedDictionary<int, string>();
        foreach (Page p in _pages)
        {
            int pageId = next++;
            int contentId = next++;
            int annotId = p.Annotation is null ? 0 : next++;
            int apId = p.Annotation is null ? 0 : next++;
            kids.Add(pageId);

            var content = new StringBuilder();
            foreach (Box r in p.Rectangles)
                content.Append("0 0 0 rg ").Append(N(r.X)).Append(' ').Append(N(r.Y)).Append(' ')
                       .Append(N(r.Width)).Append(' ').Append(N(r.Height)).Append(" re f\n");
            string c = content.ToString();

            string page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(p.Width) + " " + N(p.Height) + "]" +
                          (p.Rotate != 0 ? " /Rotate " + p.Rotate.ToString(CultureInfo.InvariantCulture) : "") +
                          " /Resources << >> /Contents " + contentId + " 0 R" +
                          (annotId != 0 ? " /Annots [" + annotId + " 0 R]" : "") + " >>";
            bodies[pageId] = page;
            bodies[contentId] = "<< /Length " + Encoding.ASCII.GetByteCount(c) + " >>\nstream\n" + c + "endstream";
            if (p.Annotation is Box a)
            {
                bodies[annotId] = "<< /Type /Annot /Subtype /Square /F 4 /Rect [" + N(a.X) + " " + N(a.Y) + " " +
                                  N(a.X + a.Width) + " " + N(a.Y + a.Height) + "] /AP << /N " + apId + " 0 R >> >>";
                string ap = "0 0 0 rg 0 0 " + N(a.Width) + " " + N(a.Height) + " re f\n";
                bodies[apId] = "<< /Type /XObject /Subtype /Form /BBox [0 0 " + N(a.Width) + " " + N(a.Height) +
                               "] /Resources << >> /Length " + Encoding.ASCII.GetByteCount(ap) + " >>\nstream\n" + ap + "endstream";
            }
        }
        bodies[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        bodies[2] = "<< /Type /Pages /Kids [" + string.Join(" ", kids.Select(k => k + " 0 R")) + "] /Count " + kids.Count + " >>";

        var output = new MemoryStream();
        void Write(string s) { byte[] b = Encoding.ASCII.GetBytes(s); output.Write(b, 0, b.Length); }
        Write("%PDF-1.7\n");
        var offsets = new long[next];
        foreach (KeyValuePair<int, string> o in bodies)
        {
            offsets[o.Key] = output.Position;
            Write(o.Key + " 0 obj\n" + o.Value + "\nendobj\n");
        }
        long xref = output.Position;
        Write("xref\n0 " + next + "\n0000000000 65535 f \n");
        for (int i = 1; i < next; i++) Write(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size " + next + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return output.ToArray();
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
