using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Chuvadi.Markup.Internal
{
    internal static class TextInput
    {
        /// <summary>Reads a reader to the end without ever buffering more than maxChars + 1 chars.</summary>
        public static string ReadAll(TextReader reader, int maxChars, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var buf = new char[8192];
            int n;
            while ((n = reader.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if ((long)sb.Length + n > maxChars)
                    throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxInputChars), maxChars, (long)sb.Length + n);
                sb.Append(buf, 0, n);
            }
            return sb.ToString();
        }
    }
}
