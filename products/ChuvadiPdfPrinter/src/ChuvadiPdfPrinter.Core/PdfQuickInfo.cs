using System;
using System.IO;
using System.Text;

namespace ChuvadiPdfPrinter
{
    /// <summary>Cheap checks on a PDF file without parsing it (Chuvadi.Pdf does the real parsing).</summary>
    public static class PdfQuickInfo
    {
        /// <summary>True when the file starts with "%PDF-" and has "%%EOF" in its last kilobyte.</summary>
        public static bool LooksLikePdf(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 8) return false;
            var head = new byte[5];
            if (stream.Read(head, 0, 5) != 5 || Encoding.ASCII.GetString(head) != "%PDF-") return false;
            int tailLength = (int)Math.Min(1024, stream.Length);
            stream.Seek(-tailLength, SeekOrigin.End);
            var tail = new byte[tailLength];
            int read = 0;
            while (read < tailLength)
            {
                int n = stream.Read(tail, read, tailLength - read);
                if (n == 0) break;
                read += n;
            }
            return Encoding.ASCII.GetString(tail, 0, read).Contains("%%EOF", StringComparison.Ordinal);
        }

        /// <summary>
        /// Best-effort page count: the "/Count n" of the page tree root when it appears uncompressed, else the
        /// number of "/Type /Page" objects. Returns 0 when neither is visible (compressed object streams).
        /// </summary>
        public static int CountPages(string path)
        {
            string text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            int best = 0;
            int at = 0;
            while ((at = text.IndexOf("/Type", at, StringComparison.Ordinal)) >= 0)
            {
                int i = at + 5;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\r' || text[i] == '\n' || text[i] == '\t')) i++;
                if (string.CompareOrdinal(text, i, "/Pages", 0, 6) == 0)
                {
                    int count = FindCountNear(text, at);
                    if (count > best) best = count;
                }
                at = i;
            }
            if (best > 0) return best;
            int pages = 0;
            at = 0;
            while ((at = text.IndexOf("/Page", at, StringComparison.Ordinal)) >= 0)
            {
                int end = at + 5;
                bool isPage = end >= text.Length || !(char.IsLetterOrDigit(text[end]));
                int before = at - 1;
                while (before >= 0 && (text[before] == ' ' || text[before] == '\r' || text[before] == '\n')) before--;
                if (isPage && before >= 4 && string.CompareOrdinal(text, before - 4, "/Type", 0, 5) == 0) pages++;
                at = end;
            }
            return pages;
        }

        private static int FindCountNear(string text, int typeIndex)
        {
            int start = text.LastIndexOf("<<", typeIndex, StringComparison.Ordinal);
            int end = text.IndexOf(">>", typeIndex, StringComparison.Ordinal);
            if (start < 0 || end < 0) return 0;
            int count = text.IndexOf("/Count", start, end - start, StringComparison.Ordinal);
            if (count < 0) return 0;
            int i = count + 6, value = 0;
            while (i < end && text[i] == ' ') i++;
            while (i < end && char.IsDigit(text[i])) value = Math.Min(1_000_000, value * 10 + (text[i++] - '0'));
            return value;
        }
    }
}
