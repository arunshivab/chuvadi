namespace Chuvadi.OpenType.Woff;

/// <summary>Limits that keep a damaged or hostile WOFF/WOFF2 file from forcing huge allocations.</summary>
internal static class WoffLimits
{
    // TRACE R-OT-0038
    /// <summary>The largest decoded font (all tables, uncompressed) a WOFF or WOFF2 file may declare: 256 MiB.</summary>
    internal const long MaxDecodedBytes = 256L * 1024 * 1024;
}
