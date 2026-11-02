using System.IO;
using System.Threading;

namespace Chuvadi.Layout.Docs
{
    /// <summary>Helpers this package keeps its own copy of, so that it needs only the engine's public API.</summary>
    internal static class Shared
    {
        /// <summary>Throws <see cref="LayoutLimitExceededException"/> when <paramref name="actual"/> exceeds <paramref name="limit"/>.</summary>
        public static void Check(string name, long limit, long actual)
        {
            if (actual > limit)
            {
                throw new LayoutLimitExceededException(name, limit, actual);
            }
        }

        /// <summary>Reads a stream to its end (at most <paramref name="limit"/> bytes, checked as it reads) and leaves it open.</summary>
        public static byte[] ReadAll(Stream s, long limit, CancellationToken ct)
        {
            if (s.CanSeek)
            {
                Check(nameof(LayoutLimits.MaxSourceBytes), limit, s.Length - s.Position);
            }
            using MemoryStream ms = new MemoryStream();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                Check(nameof(LayoutLimits.MaxSourceBytes), limit, ms.Length + read);
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }
    }
}
