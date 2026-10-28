using System;
using System.IO;
using System.Text;

namespace Chuvadi.Zip.Internal;

/// <summary>Entry-name validation and extraction-path containment ("zip-slip" defence).</summary>
internal static class PathSafety
{
    /// <summary>
    /// Normalises an entry name to forward-slash form. Returns false (with a reason) for names that
    /// are rooted, contain "..", a drive/stream colon, or a NUL. "." segments and repeated slashes
    /// collapse. A result of "" means "the archive root itself" (e.g. "./") and is not an error.
    /// </summary>
    public static bool TryNormalize(string name, out string normalized, out bool isDirectory, out string? error)
    {
        normalized = string.Empty;
        isDirectory = false;
        error = null;

        if (string.IsNullOrEmpty(name)) { error = "Entry name is empty."; return false; }
        if (name.IndexOf('\0') >= 0) { error = "Entry name contains a NUL character."; return false; }

        string s = name.Replace('\\', '/');
        if (s[0] == '/') { error = "Entry name is an absolute path."; return false; }
        isDirectory = s[s.Length - 1] == '/';

        var sb = new StringBuilder(s.Length);
        foreach (var seg in s.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") { error = "Entry name contains a '..' segment."; return false; }
            if (seg.IndexOf(':') >= 0) { error = "Entry name contains ':' (drive letter or alternate data stream)."; return false; }
            if (sb.Length > 0) sb.Append('/');
            sb.Append(seg);
        }
        if (sb.Length > 0 && isDirectory) sb.Append('/');
        normalized = sb.ToString();
        return true;
    }

    public static int Depth(string normalized)
    {
        int d = 0;
        foreach (char c in normalized) if (c == '/') d++;
        return normalized.EndsWith('/') ? d : d + 1;
    }

    public static StringComparison FileSystemComparison
        => (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolves <paramref name="normalized"/> under <paramref name="rootFull"/>; null if it would escape.</summary>
    public static string? ResolveUnderRoot(string rootFull, string normalized)
    {
        string combined = Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar));
        string full = Path.GetFullPath(combined);
        string rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        var cmp = FileSystemComparison;
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), rootFull.TrimEnd(Path.DirectorySeparatorChar), cmp))
            return full;
        return full.StartsWith(rootWithSep, cmp) ? full : null;
    }
}
