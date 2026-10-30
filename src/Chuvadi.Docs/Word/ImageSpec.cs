using System;
using System.IO;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Describes an image to insert into a document. Create via the factory methods, which give
/// you the full range of options:
///
/// <list type="bullet">
/// <item><see cref="FromFile(string)"/> — auto-size from the file's pixel dimensions and DPI.</item>
/// <item><see cref="FromFile(string, double, double)"/> — explicit display size in points.</item>
/// <item><see cref="FromBytes(byte[], string)"/> — auto-size from raw bytes.</item>
/// <item><see cref="Inline(byte[], string, double, double)"/> — explicit inline image.</item>
/// <item><see cref="Float(byte[], string, double, double, FloatingPosition)"/> — floating image.</item>
/// </list>
///
/// Any spec can be made floating by setting <see cref="Placement"/> and <see cref="Position"/>,
/// or by calling <see cref="AsFloating(FloatingPosition)"/>.
/// </summary>
public sealed class ImageSpec
{
    /// <summary>Raw image bytes (empty for a picture that is only linked, see <see cref="LinkUrl"/>). Loaded documents share one array per media part.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>MIME type, e.g. "image/png". Detected automatically by the factory methods.</summary>
    public required string ContentType { get; init; }

    /// <summary>Display width in points.</summary>
    public required double WidthPt { get; set; }

    /// <summary>Display height in points.</summary>
    public required double HeightPt { get; set; }

    /// <summary>Optional alt text / accessibility description. Also used as the match key for
    /// floating-image template replacement.</summary>
    public string? AltText { get; set; }

    /// <summary>Inline (default) or floating.</summary>
    public ImagePlacement Placement { get; set; } = ImagePlacement.Inline;

    /// <summary>Position for floating images; required when <see cref="Placement"/> is Floating.</summary>
    public FloatingPosition? Position { get; set; }

    /// <summary>Shape name (wp:docPr/@name). Null = "Picture N".</summary>
    public string? Name { get; set; }

    /// <summary>Accessibility title (wp:docPr/@title).</summary>
    public string? Title { get; set; }

    /// <summary>Hidden (wp:docPr/@hidden): kept in the file but not shown.</summary>
    public bool Hidden { get; set; }

    /// <summary>Cropping of the picture (a:srcRect), as fractions of each side; null = none.</summary>
    public ImageCrop? Crop { get; set; }

    /// <summary>Clockwise rotation in degrees (a:xfrm/@rot).</summary>
    public double RotationDegrees { get; set; }

    /// <summary>Mirrored left-to-right (a:xfrm/@flipH).</summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>Mirrored top-to-bottom (a:xfrm/@flipV).</summary>
    public bool FlipVertical { get; set; }

    /// <summary>
    /// Extra space the picture's effects (shadow, glow, rotation) take outside its frame, in points
    /// (wp:effectExtent). Layout reserves it around the image.
    /// </summary>
    public ImageEffectExtent? EffectExtent { get; set; }

    /// <summary>Width as a percentage of a page frame (wp14:sizeRelH); <see cref="WidthPt"/> stays the fallback size.</summary>
    public RelativeSize? RelativeWidth { get; set; }

    /// <summary>Height as a percentage of a page frame (wp14:sizeRelV); <see cref="HeightPt"/> stays the fallback size.</summary>
    public RelativeSize? RelativeHeight { get; set; }

    /// <summary>
    /// A linked picture (a:blip/@r:link): the external file or URL Word shows. With empty
    /// <see cref="Bytes"/> only the link is written ("link to file"); with bytes too, both ("link and save").
    /// </summary>
    public string? LinkUrl { get; set; }

    /// <summary>Space between an INLINE picture and the text, in points (wp:inline/@distT…). Floating pictures use <see cref="FloatingPosition"/>.</summary>
    public ImageEffectExtent? InlineDistance { get; set; }

    // ---- Factory methods ------------------------------------------------------------

    /// <summary>Loads an image file and auto-sizes it from its pixel dimensions and DPI.</summary>
    public static ImageSpec FromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return FromBytes(bytes, ResolveContentType(bytes, path));
    }

    /// <summary>Loads an image file with an explicit display size in points.</summary>
    public static ImageSpec FromFile(string path, double widthPt, double heightPt)
    {
        var bytes = File.ReadAllBytes(path);
        return new ImageSpec
        {
            Bytes = bytes,
            ContentType = ResolveContentType(bytes, path),
            WidthPt = widthPt,
            HeightPt = heightPt,
        };
    }

    /// <summary>Creates a spec from raw bytes, auto-sizing from the image's own dimensions/DPI.</summary>
    public static ImageSpec FromBytes(byte[] bytes, string? contentType = null)
    {
        if (bytes is null || bytes.Length == 0) throw new ArgumentException("Image bytes required.", nameof(bytes));
        var info = ImageMetadata.Inspect(bytes);
        var ct = contentType ?? info.ContentType;
        if (info.PixelWidth == 0 || info.PixelHeight == 0)
            throw new InvalidOperationException(
                "This image format carries no pixel dimensions (e.g. EMF/WMF). Supply explicit widthPt/heightPt via Inline(...) or FromFile(path, widthPt, heightPt).");
        // A corrupt or hostile header (e.g. a PNG claiming 0xFFFFFFFF pixels) gives no usable size.
        if (info.PixelWidth < 0 || info.PixelHeight < 0 || !(double.IsFinite(info.WidthPt) && double.IsFinite(info.HeightPt) && info.WidthPt > 0 && info.HeightPt > 0))
            throw new InvalidOperationException("The image header gives no valid size. Supply explicit widthPt/heightPt via Inline(...).");
        return new ImageSpec
        {
            Bytes = bytes,
            ContentType = ct,
            WidthPt = info.WidthPt,
            HeightPt = info.HeightPt,
        };
    }

    /// <summary>Creates an inline image with an explicit display size in points.</summary>
    public static ImageSpec Inline(byte[] bytes, string contentType, double widthPt, double heightPt)
    {
        if (bytes is null || bytes.Length == 0) throw new ArgumentException("Image bytes required.", nameof(bytes));
        return new ImageSpec
        {
            Bytes = bytes,
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType)),
            WidthPt = widthPt,
            HeightPt = heightPt,
        };
    }

    /// <summary>Creates a floating image with an explicit size and position.</summary>
    public static ImageSpec Float(byte[] bytes, string contentType, double widthPt, double heightPt, FloatingPosition position)
    {
        if (position is null) throw new ArgumentNullException(nameof(position));
        return new ImageSpec
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes)),
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType)),
            WidthPt = widthPt,
            HeightPt = heightPt,
            Placement = ImagePlacement.Floating,
            Position = position,
        };
    }

    /// <summary>Returns this spec converted to a floating image at the given position.</summary>
    public ImageSpec AsFloating(FloatingPosition position)
    {
        Placement = ImagePlacement.Floating;
        Position = position ?? throw new ArgumentNullException(nameof(position));
        return this;
    }

    /// <summary>Scales the display size to a target width in points, preserving aspect ratio.</summary>
    public ImageSpec ScaleToWidth(double widthPt)
    {
        if (WidthPt > 0) { HeightPt = HeightPt * (widthPt / WidthPt); WidthPt = widthPt; }
        return this;
    }

    /// <summary>Scales the display size to a target height in points, preserving aspect ratio.</summary>
    public ImageSpec ScaleToHeight(double heightPt)
    {
        if (HeightPt > 0) { WidthPt = WidthPt * (heightPt / HeightPt); HeightPt = heightPt; }
        return this;
    }

    private static string ResolveContentType(byte[] bytes, string path)
    {
        // Prefer magic-byte detection; fall back to the file extension.
        return ImageMetadata.DetectContentType(bytes)
            ?? ImageMetadata.ContentTypeForExtension(Path.GetExtension(path))
            ?? throw new InvalidDataException($"Could not determine image type for '{path}'.");
    }
}
