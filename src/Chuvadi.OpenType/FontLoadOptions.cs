using System;

namespace Chuvadi.OpenType;

/// <summary>Options for loading a font (P5, D-205).</summary>
public sealed record FontLoadOptions
{
    /// <summary>The default options: a 256 MiB limit on decoded WOFF/WOFF2 data.</summary>
    public static FontLoadOptions Default { get; } = new FontLoadOptions();

    /// <summary>
    /// The largest decoded font a WOFF or WOFF2 file may declare (all tables, uncompressed), in bytes. A file declaring
    /// more is refused with <see cref="OpenTypeFormatException"/>. Plain sfnt fonts and collections are not decoded, so
    /// the limit does not apply to them. Default 256 MiB; must be greater than 0.
    /// </summary>
    public long MaxDecodedBytes { get; init; } = Woff.WoffLimits.DefaultMaxDecodedBytes;

    internal long Validate()
    {
        if (MaxDecodedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDecodedBytes), MaxDecodedBytes, "MaxDecodedBytes must be greater than 0.");
        }

        return MaxDecodedBytes;
    }
}
