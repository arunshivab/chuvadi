namespace Chuvadi.OpenType.Woff;

/// <summary>Limits that keep a damaged or hostile WOFF/WOFF2 file from forcing huge allocations.</summary>
internal static class WoffLimits
{
    // TRACE R-OT-0038 (superseded by R-OT-0057)
    // TRACE R-OT-0057
    /// <summary>The default largest decoded font (all tables, uncompressed) a WOFF or WOFF2 file may declare: 256 MiB.
    /// The limit used is <see cref="FontLoadOptions.MaxDecodedBytes"/>.</summary>
    internal const long DefaultMaxDecodedBytes = 256L * 1024 * 1024;
}
