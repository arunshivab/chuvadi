using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ChuvadiPdfPrinter
{
    /// <summary>Builds output file names from a pattern and places files without ever overwriting by accident.</summary>
    public static class OutputNamer
    {
        /// <summary>Longest base name produced (Windows paths stay well under the 260-character limit).</summary>
        public const int MaxBaseNameLength = 120;

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// Expands {title} {user} {machine} {job} {date} {date:format} and makes the result a valid Windows file
        /// name. The original title and user are kept unchanged in the audit trail; only the file name is
        /// adjusted, because Windows forbids characters such as \ / : * ? " &lt; &gt; |.
        /// </summary>
        public static string Format(string pattern, JobInfo job, DateTimeOffset now)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            if (job is null) throw new ArgumentNullException(nameof(job));
            var sb = new StringBuilder();
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                if (c != '{')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                int close = pattern.IndexOf('}', i + 1);
                if (close < 0)
                {
                    sb.Append(pattern, i, pattern.Length - i);
                    break;
                }
                string token = pattern.Substring(i + 1, close - i - 1);
                sb.Append(Expand(token, job, now) ?? "{" + token + "}");
                i = close + 1;
            }
            return SanitizeFileName(sb.ToString());
        }

        private static string? Expand(string token, JobInfo job, DateTimeOffset now)
        {
            string name = token, format = string.Empty;
            int colon = token.IndexOf(':');
            if (colon >= 0)
            {
                name = token.Substring(0, colon);
                format = token.Substring(colon + 1);
            }
            switch (name.ToLowerInvariant())
            {
                case "title": return string.IsNullOrWhiteSpace(job.Title) ? "Untitled" : StripPath(job.Title);
                case "user": return string.IsNullOrWhiteSpace(job.UserName) ? "unknown" : PrinterConfig.StripDomain(job.UserName);
                case "machine": return job.MachineName.TrimStart('\\');
                case "job": return job.JobId.ToString(CultureInfo.InvariantCulture);
                case "date":
                    try
                    {
                        return now.ToString(format.Length == 0 ? "yyyy-MM-dd HHmmss" : format, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                default: return null;
            }
        }

        /// <summary>
        /// Applications often use a full path or URL as the document title; keep only its last part. Titles
        /// that merely contain a slash ("A/B results") are left whole.
        /// </summary>
        private static string StripPath(string title)
        {
            bool isPath = title.Contains('\\') || title.Contains("://", StringComparison.Ordinal);
            int slash = isPath ? Math.Max(title.LastIndexOf('\\'), title.LastIndexOf('/')) : -1;
            string name = slash >= 0 && slash < title.Length - 1 ? title.Substring(slash + 1) : title;
            return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        /// <summary>
        /// Replaces characters Windows does not allow in file names with '_', removes trailing dots and spaces
        /// (Windows drops them), avoids reserved device names and caps the length.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c < 32 || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
            string result = sb.ToString();
            if (result.Length > MaxBaseNameLength) result = result.Substring(0, MaxBaseNameLength);
            result = result.TrimEnd('.', ' ');
            if (result.Length == 0) result = "Untitled";
            string stem = result;
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem.Substring(0, dot);
            foreach (string reserved in ReservedNames)
                if (string.Equals(stem.TrimEnd(' '), reserved, StringComparison.OrdinalIgnoreCase)) return "_" + result;
            return result;
        }

        /// <summary>
        /// Moves <paramref name="sourcePath"/> to <paramref name="folder"/>\&lt;baseName&gt;.pdf following
        /// <paramref name="policy"/>. A name is claimed by creating the file exclusively (atomic on every OS), then
        /// the PDF is moved over the claimed file, so concurrent jobs never share a name.
        /// Returns the final path.
        /// </summary>
        public static string Place(string sourcePath, string folder, string baseName, CollisionPolicy policy)
        {
            if (sourcePath is null) throw new ArgumentNullException(nameof(sourcePath));
            if (folder is null) throw new ArgumentNullException(nameof(folder));
            Directory.CreateDirectory(folder);
            string first = Path.Combine(folder, baseName + ".pdf");
            switch (policy)
            {
                case CollisionPolicy.Overwrite:
                    File.Move(sourcePath, first, overwrite: true);
                    return first;
                case CollisionPolicy.Fail:
                    if (!TryReserve(first)) throw new IOException("Output file already exists: " + first);
                    MoveOntoReserved(sourcePath, first);
                    return first;
                default:
                    for (int n = 1; n < 10_000; n++)
                    {
                        string candidate = n == 1 ? first : Path.Combine(folder, baseName + " (" + n.ToString(CultureInfo.InvariantCulture) + ").pdf");
                        if (!TryReserve(candidate)) continue; // taken, possibly by a concurrent job
                        MoveOntoReserved(sourcePath, candidate);
                        return candidate;
                    }
                    throw new IOException("No free file name for '" + baseName + "' in " + folder);
            }
        }

        /// <summary>Claims <paramref name="path"/> by creating it exclusively; false when it already exists.</summary>
        private static bool TryReserve(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }

        private static void MoveOntoReserved(string sourcePath, string reserved)
        {
            try
            {
                File.Move(sourcePath, reserved, overwrite: true);
            }
            catch
            {
                try { File.Delete(reserved); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
