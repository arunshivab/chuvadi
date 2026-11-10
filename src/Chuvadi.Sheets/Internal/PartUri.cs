using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Relationship targets to part names (Sheets 2.0.2, G1). OPC Part 2 §9.3: a target starting with
/// "/" is relative to the package root, any other target to the source part's folder. Dot segments
/// ("." and "..") are then removed as in RFC 3986 §5.2.4, which OPC uses; a ".." above the root stops
/// at the root. A name without dot segments is returned exactly as before 2.0.2, so every target
/// that resolved then resolves to the same part now.
/// </summary>
internal static class PartUri
{
    /// <summary>True if <paramref name="path"/> has a "." or ".." segment.</summary>
    public static bool HasDotSegment(string path)
    {
        int start = 0;
        for (int i = 0; i <= path.Length; i++)
        {
            if (i < path.Length && path[i] != '/') continue;
            int len = i - start;
            if ((len == 1 && path[start] == '.') || (len == 2 && path[start] == '.' && path[start + 1] == '.')) return true;
            start = i + 1;
        }
        return false;
    }

    /// <summary>
    /// <paramref name="path"/> (starting with "/") with its dot segments removed. Other segments,
    /// empty ones included, are kept as they are.
    /// </summary>
    public static string RemoveDotSegments(string path)
    {
        var output = new List<string>();
        var segments = path.Split('/');
        for (int i = 1; i < segments.Length; i++)   // segments[0] is the empty text before the leading "/"
        {
            var seg = segments[i];
            bool last = i == segments.Length - 1;
            if (seg == ".")
            {
                if (last) output.Add("");
            }
            else if (seg == "..")
            {
                if (output.Count > 0) output.RemoveAt(output.Count - 1);
                if (last) output.Add("");
            }
            else output.Add(seg);
        }
        return "/" + string.Join("/", output);
    }

    /// <summary>Removes dot segments only when there are any (otherwise the name is unchanged).</summary>
    public static string Normalize(string path) => HasDotSegment(path) ? RemoveDotSegments(path) : path;
}
