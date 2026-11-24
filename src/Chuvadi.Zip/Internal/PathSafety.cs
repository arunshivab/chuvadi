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


    /// <summary>
    /// Whether extraction applies the Windows file-name rules. Defaults to the running OS; internal so the
    /// tests can exercise the rules on any platform.
    /// </summary>
    internal static bool ApplyWindowsRules { get; set; } = OperatingSystem.IsWindows();

    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM\u00B9", "COM\u00B2", "COM\u00B3", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
    };

    /// <summary>
    /// Returns why a normalised entry name is not safe to create on Windows, or null if it is fine:
    /// a device name (CON, NUL, COM1 ..., also with an extension), a segment ending in a dot or a space
    /// (Windows silently strips those, so the file lands under another name), or a character that is
    /// invalid in Windows names.
    /// </summary>
    internal static string? WindowsReason(string normalized)
    {
        foreach (var seg in normalized.Split('/'))
        {
            if (seg.Length == 0) continue;
            foreach (char c in seg)
                if (c < ' ' || c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*')
                    return "Entry name contains a character that is not valid in Windows file names.";
            char last = seg[seg.Length - 1];
            if (last == '.' || last == ' ')
                return "Entry name has a segment ending in a dot or a space, which Windows strips.";
            int dot = seg.IndexOf('.');
            string stem = (dot < 0 ? seg : seg.Substring(0, dot)).TrimEnd(' ');
            foreach (var dev in ReservedDeviceNames)
                if (string.Equals(stem, dev, StringComparison.OrdinalIgnoreCase))
                    return "Entry name is a reserved Windows device name.";
        }
        return null;
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
