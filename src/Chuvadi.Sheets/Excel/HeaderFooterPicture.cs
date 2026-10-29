using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>Which third of a header or footer a picture (or text) belongs to.</summary>
public enum HeaderFooterSection { Left = 0, Center, Right }

/// <summary>Header or footer.</summary>
public enum HeaderFooterPart { Header = 0, Footer }

/// <summary>Which pages a header or footer applies to (see <see cref="HeaderFooterText.DifferentOddEven"/> and <see cref="HeaderFooterText.DifferentFirst"/>).</summary>
public enum HeaderFooterPageKind { Odd = 0, Even, First }

/// <summary>
/// A picture in a header or footer (the &amp;G code), since 1.7.0. Read from the sheet's
/// legacyDrawingHF VML part; read-only and not written on save. The &amp;G in the matching
/// header/footer text marks where it is drawn.
/// </summary>
public sealed class HeaderFooterPicture
{
    internal HeaderFooterPicture() { }

    /// <summary>The VML shape id as stored: "LH", "CH", "RH", "LF", "CF", "RF", with "EVEN" or "FIRST" appended for those pages.</summary>
    public string Id { get; internal set; } = string.Empty;

    public HeaderFooterSection Section { get; internal set; }
    public HeaderFooterPart Part { get; internal set; }
    public HeaderFooterPageKind Kind { get; internal set; }

    /// <summary>The picture's title (o:title), often the original file name, or null.</summary>
    public string? Title { get; internal set; }

    /// <summary>Size in points from the shape's style, or null when not stated.</summary>
    public double? WidthPoints { get; internal set; }
    public double? HeightPoints { get; internal set; }

    /// <summary>The image part's name, e.g. "/xl/media/image1.png", or null.</summary>
    public string? ImagePartName { get; internal set; }

    /// <summary>The image's content type, e.g. "image/png", or null.</summary>
    public string? ContentType { get; internal set; }

    /// <summary>For a linked picture: the external target, or null.</summary>
    public string? LinkedImageTarget { get; internal set; }

    internal Func<byte[]?>? ImageLoader { get; set; }
    internal byte[]? ImageData { get; set; }

    /// <summary>
    /// The image file's bytes as stored, or null when linked or not loaded (see
    /// <see cref="XlsxReaderOptions.LoadImageData"/>). For the streaming reader they are read
    /// on demand while the <see cref="XlsxReader"/> is open. Treat the array as read-only.
    /// </summary>
    public byte[]? GetImageBytes() => ImageData ?? ImageLoader?.Invoke();

    internal static bool TryParsePosition(string id, out HeaderFooterSection section, out HeaderFooterPart part, out HeaderFooterPageKind kind)
    {
        section = default; part = default; kind = HeaderFooterPageKind.Odd;
        if (id.Length < 2) return false;
        switch (char.ToUpperInvariant(id[0]))
        {
            case 'L': section = HeaderFooterSection.Left; break;
            case 'C': section = HeaderFooterSection.Center; break;
            case 'R': section = HeaderFooterSection.Right; break;
            default: return false;
        }
        switch (char.ToUpperInvariant(id[1]))
        {
            case 'H': part = HeaderFooterPart.Header; break;
            case 'F': part = HeaderFooterPart.Footer; break;
            default: return false;
        }
        var rest = id.Substring(2);
        if (rest.Length == 0) return true;
        if (rest.Equals("EVEN", StringComparison.OrdinalIgnoreCase)) { kind = HeaderFooterPageKind.Even; return true; }
        if (rest.Equals("FIRST", StringComparison.OrdinalIgnoreCase)) { kind = HeaderFooterPageKind.First; return true; }
        return false;
    }

    public override string ToString() => $"{Id} {ImagePartName}";
}
