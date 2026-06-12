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
    /// <summary>Raw image bytes.</summary>
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
