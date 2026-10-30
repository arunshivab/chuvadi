using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Conversion.Internal;

/// <summary>URL checks shared by the importers and exporters.</summary>
internal static class Urls
{
    private static readonly HashSet<string> s_exportSchemes = new(StringComparer.Ordinal) { "http", "https", "mailto", "tel", "ftp" };

    /// <summary>Picture types every browser shows (others are written as their alternative text).</summary>
    public static readonly HashSet<string> BrowserImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/bmp", "image/webp",
    };

    /// <summary>
    /// A link that cannot run or reach anything unexpected: an allowed scheme, or (when allowed) a
    /// relative path. Control characters, backslashes (UNC paths, which Word opens over SMB) and
    /// scheme-relative "//host" links are refused.
    /// </summary>
    public static bool IsSafeLink(string url, ICollection<string> schemes, bool allowRelative)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.Trim();
        foreach (char c in u)
            if (char.IsControl(c) || c == '\\' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) return false;
        if (u.StartsWith("//", StringComparison.Ordinal)) return false;
        int colon = u.IndexOf(':');
        int stop = u.IndexOfAny(new[] { '/', '?', '#' });
        if (colon < 0 || (stop >= 0 && stop < colon)) return allowRelative;
        var scheme = u.Substring(0, colon).ToLowerInvariant();
        return schemes.Contains(scheme);
    }

    /// <summary>The links exporters write: http(s), mailto, tel, ftp and relative paths.</summary>
    public static bool IsSafeExportLink(string url) => IsSafeLink(url, s_exportSchemes, allowRelative: true);
}
