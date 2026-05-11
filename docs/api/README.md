# API Reference

Auto-generated from XML doc comments. One file per public type, grouped by module.

Regenerate with:

```bash
python tools/gen_api_docs.py
```

## Chuvadi.Pdf.

| Type | Kind | Description |
|---|---|---|
| [Class1](/Class1.md) | class | — |

## Chuvadi.Pdf.Annotations

| Type | Kind | Description |
|---|---|---|
| [AnnotationException](Annotations/AnnotationException.md) | class | Thrown when an annotation operation fails. |
| [AnnotationReader](Annotations/AnnotationReader.md) | class | Reads annotations from a PDF document. |
| [AnnotationType](Annotations/AnnotationType.md) | enum | PDF annotation subtype. |
| [AnnotationWriter](Annotations/AnnotationWriter.md) | class | Adds new annotations to a PDF document and writes the result. |
| [FreeTextAnnotation](Annotations/FreeTextAnnotation.md) | class | Free-text annotation drawn directly on the page (§12.5.6.6). |
| [GenericAnnotation](Annotations/GenericAnnotation.md) | class | Catch-all annotation for subtypes not specifically modelled. |
| [InkAnnotation](Annotations/InkAnnotation.md) | class | Free-hand ink annotation (§12.5.6.13). |
| [LinkAnnotation](Annotations/LinkAnnotation.md) | class | Hyperlink annotation (§12.5.6.5). |
| [MarkupAnnotation](Annotations/MarkupAnnotation.md) | class | Text-markup annotation (§12.5.6.10): Highlight, Underline, Squiggly, or StrikeOut. |
| [PdfAnnotation](Annotations/PdfAnnotation.md) | class | Base class for all modelled annotations. |
| [StampAnnotation](Annotations/StampAnnotation.md) | class | Rubber-stamp annotation (§12.5.6.12), e.g., "Approved", "Confidential". |
| [TextAnnotation](Annotations/TextAnnotation.md) | class | Sticky-note text annotation (§12.5.6.4). |

## Chuvadi.Pdf.Content

| Type | Kind | Description |
|---|---|---|
| [ContentException](Content/ContentException.md) | class | Thrown when a PDF content stream contains invalid or unsupported operators. |
| [ContentStreamParser](Content/ContentStreamParser.md) | class | Parses a PDF content stream and extracts text fragments with their approximate positions. |
| [GraphicsState](Content/GraphicsState.md) | class | Represents the graphics and text state at a point in content stream processing. |
| [Matrix3x3](Content/Matrix3x3.md) | struct | A 3x3 matrix used for 2D affine transformations in PDF user space. |
| [TextFragment](Content/TextFragment.md) | class | A piece of text extracted from a PDF content stream, together with its approximate position in user space. |

## Chuvadi.Pdf.Documents

| Type | Kind | Description |
|---|---|---|
| [PdfDocument](Documents/PdfDocument.md) | class | Represents an opened PDF document. |
| [PdfDocumentException](Documents/PdfDocumentException.md) | class | Thrown when the PDF document model encounters an invalid or unsupported structure, such as a malformed page tree or a missing required entry. |
| [PdfPage](Documents/PdfPage.md) | class | Represents a single page in a PDF document. |
| [PdfPageCollection](Documents/PdfPageCollection.md) | class | Provides lazy, random-access to the pages of a PDF document. |
| [PdfRectangle](Documents/PdfRectangle.md) | struct | An immutable rectangle in PDF user space (points, 1/72 inch). |

## Chuvadi.Pdf.Filters

| Type | Kind | Description |
|---|---|---|
| [Adler32](Filters/Adler32.md) | class | Computes and verifies Adler-32 checksums as defined in RFC 1950. |
| [Ascii85Filter](Filters/Ascii85Filter.md) | class | Implements the PDF ASCII85Decode filter. 4 binary bytes → 5 ASCII characters. |
| [AsciiHexFilter](Filters/AsciiHexFilter.md) | class | Implements the PDF ASCIIHexDecode filter. |
| [DeflateFilter](Filters/DeflateFilter.md) | class | Implements the PDF FlateDecode filter using zlib-framed DEFLATE. |
| [FilterException](Filters/FilterException.md) | class | Thrown when a PDF stream filter encounters data it cannot decode or encode. |
| [FilterParameters](Filters/FilterParameters.md) | record | Parameters passed to a filter's Decode or Encode operation, derived from the `/DecodeParms` or `/EncodeParms` dictionary in the stream dictionary. |
| [FilterPipeline](Filters/FilterPipeline.md) | class | Applies and removes chains of PDF stream filters. |
| [FilterRegistry](Filters/FilterRegistry.md) | class | Central registry of PDF stream filter implementations. |
| [IStreamFilter](Filters/IStreamFilter.md) | interface | Defines the contract for a PDF stream filter. |
| [LzwFilter](Filters/LzwFilter.md) | class | Implements the PDF LZWDecode filter. |
| [RunLengthFilter](Filters/RunLengthFilter.md) | class | Implements the PDF RunLengthDecode filter (PackBits algorithm). |

## Chuvadi.Pdf.Fonts

| Type | Kind | Description |
|---|---|---|
| [CMapParser](Fonts/CMapParser.md) | class | Parses a PDF ToUnicode CMap stream and builds a character code to Unicode string mapping. |
| [FontException](Fonts/FontException.md) | class | Thrown when a font dictionary cannot be parsed or a character code cannot be mapped to a Unicode codepoint. |
| [FontRenderer](Fonts/FontRenderer.md) | class | High-level API for extracting glyph outlines from a TrueType or OpenType font. |
| [FontRenderingException](Fonts/FontRenderingException.md) | class | Thrown when a font file cannot be parsed or a glyph outline cannot be extracted due to an invalid or unsupported font structure. |
| [GlyphMetrics](Fonts/GlyphMetrics.md) | class | Typographic metrics for a single glyph, in font units (unscaled). |
| [GlyphOutline](Fonts/GlyphOutline.md) | class | The outline of a single glyph as a `Path` of contours, together with its `GlyphMetrics`. |
| [PdfFont](Fonts/PdfFont.md) | class | Represents a PDF font and provides character code to Unicode mapping for text extraction purposes. |
| [PdfFontEncoding](Fonts/PdfFontEncoding.md) | class | Maps 1-byte character codes (0-255) to Unicode codepoints for simple fonts. |
| [TrueTypeLoader](Fonts/TrueTypeLoader.md) | class | Loads a TrueType or OpenType font from raw bytes and provides access to glyph outlines and metrics. |

## Chuvadi.Pdf.Forms

| Type | Kind | Description |
|---|---|---|
| [FormException](Forms/FormException.md) | class | Thrown when an AcroForm or outline operation fails. |
| [FormField](Forms/FormField.md) | class | A single AcroForm field, read from a PDF document. |
| [FormFieldType](Forms/FormFieldType.md) | enum | Type of an AcroForm field. |
| [FormFiller](Forms/FormFiller.md) | class | Fills AcroForm field values in a PDF document and writes the result. |
| [FormReader](Forms/FormReader.md) | class | Reads AcroForm interactive form fields from a PDF document. |
| [OutlineItem](Forms/OutlineItem.md) | class | A single bookmark in the document outline tree. |
| [OutlineReader](Forms/OutlineReader.md) | class | Reads the document outline (bookmark) tree from a PDF. |

## Chuvadi.Pdf.Graphics

| Type | Kind | Description |
|---|---|---|
| [ColorF](Graphics/ColorF.md) | struct | An immutable colour value, with support for DeviceGray, DeviceRGB, and DeviceCMYK colour spaces. |
| [ColorSpace](Graphics/ColorSpace.md) | enum | The colour space of a `ColorF` value. |
| [FillRule](Graphics/FillRule.md) | enum | Determines how the interior of a path is defined when the path self-intersects or has nested sub-paths. |
| [LineCap](Graphics/LineCap.md) | enum | Specifies the shape of the ends of open subpaths when stroked. |
| [LineJoin](Graphics/LineJoin.md) | enum | Specifies the shape of corners where two path segments meet when stroked. |
| [Path](Graphics/Path.md) | class | A mutable vector graphics path built from moveto, lineto, curve, and close operations. |
| [PathFlattener](Graphics/PathFlattener.md) | class | Flattens a `Path` containing cubic Bezier curves into a sequence of straight line segments suitable for scanline rasterization. |
| [PathSegment](Graphics/PathSegment.md) | struct | A single segment in a vector graphics path. |
| [PathSegmentKind](Graphics/PathSegmentKind.md) | enum | The kind of a `PathSegment`. |
| [PixelBuffer](Graphics/PixelBuffer.md) | class | A packed BGRA (Blue, Green, Red, Alpha) pixel buffer. |
| [PointF](Graphics/PointF.md) | struct | An immutable point in 2D user space, measured in PDF points (1/72 inch). |
| [RectangleF](Graphics/RectangleF.md) | struct | An immutable axis-aligned rectangle in PDF user space (points, 1/72 inch). |
| [SizeF](Graphics/SizeF.md) | struct | An immutable size (width × height) in PDF points (1/72 inch). |
| [StrokeStyle](Graphics/StrokeStyle.md) | class | Encapsulates all stroke properties: width, cap, join, dash pattern, and miter limit. |
| [Transform](Graphics/Transform.md) | struct | An immutable 2D affine transformation matrix. |

## Chuvadi.Pdf.IO

| Type | Kind | Description |
|---|---|---|
| [PdfReader](IO/PdfReader.md) | class | Opens an existing PDF file and provides access to its object graph. |
| [PdfReaderException](IO/PdfReaderException.md) | class | Thrown when `PdfReader` encounters a PDF file structure it cannot parse or recover from. |
| [PdfWriter](IO/PdfWriter.md) | class | Writes a complete PDF file to an output stream. |

## Chuvadi.Pdf.Images

| Type | Kind | Description |
|---|---|---|
| [BmpEncoder](Images/BmpEncoder.md) | class | Encodes an `ImageFrame` to Windows BMP format. |
| [ImageColorFormat](Images/ImageColorFormat.md) | enum | Specifies the colour format of a decoded image. |
| [ImageException](Images/ImageException.md) | class | Thrown when an image cannot be decoded or encoded due to an invalid format, unsupported feature, or data corruption. |
| [ImageFrame](Images/ImageFrame.md) | class | A decoded image frame held in a `PixelBuffer`. |
| [JpegDecoder](Images/JpegDecoder.md) | class | Decodes a baseline sequential DCT JPEG (SOF0) into an `ImageFrame`. |
| [PngDecoder](Images/PngDecoder.md) | class | Decodes a PNG image into an `ImageFrame`. |
| [PngEncoder](Images/PngEncoder.md) | class | Encodes an `ImageFrame` to PNG format. |

## Chuvadi.Pdf.Objects

| Type | Kind | Description |
|---|---|---|
| [IPdfObjectResolver](Objects/IPdfObjectResolver.md) | interface | Resolves PDF indirect object references to their primitive values. |
| [PdfIndirectObject](Objects/PdfIndirectObject.md) | class | Represents an indirect object — a `PdfPrimitive` paired with the `PdfObjectId` that identifies it in the PDF file. |
| [PdfObjectException](Objects/PdfObjectException.md) | class | Thrown when the PDF object model encounters an invalid structure, such as a malformed xref table or an unresolvable object reference. |
| [PdfObjectStore](Objects/PdfObjectStore.md) | class | In-memory store for PDF indirect objects, with lazy indirect reference resolution. |
| [XrefEntry](Objects/XrefEntry.md) | struct | Represents one entry in a PDF cross-reference table or stream. |
| [XrefEntryType](Objects/XrefEntryType.md) | enum | Identifies the type of a `XrefEntry`. |
| [XrefStreamTable](Objects/XrefStreamTable.md) | class | Reads and writes PDF 1.5+ cross-reference streams. |
| [XrefTable](Objects/XrefTable.md) | class | Represents a classic PDF cross-reference table. |

## Chuvadi.Pdf.Operations

| Type | Kind | Description |
|---|---|---|
| [OperationsException](Operations/OperationsException.md) | class | Thrown when a PDF page operation (merge, split, delete, rotate, reorder) cannot be completed due to an invalid argument or document structure. |
| [PageOperations](Operations/PageOperations.md) | class | Provides static methods for high-level PDF page operations: merge, split, delete, rotate, and reorder. |

## Chuvadi.Pdf.Primitives

| Type | Kind | Description |
|---|---|---|
| [PdfArray](Primitives/PdfArray.md) | class | Represents a PDF array object — an ordered sequence of primitives. |
| [PdfBoolean](Primitives/PdfBoolean.md) | class | Represents a PDF boolean value (`true` or `false`). |
| [PdfDictionary](Primitives/PdfDictionary.md) | class | Represents a PDF dictionary object — a map from `PdfName` keys to `PdfPrimitive` values. |
| [PdfInteger](Primitives/PdfInteger.md) | class | Represents a PDF integer object. |
| [PdfName](Primitives/PdfName.md) | class | Represents a PDF name object (e.g. `/Type`, `/Page`). |
| [PdfNull](Primitives/PdfNull.md) | class | Represents the PDF null object. |
| [PdfPrimitive](Primitives/PdfPrimitive.md) | class | Abstract base class for all PDF primitive object types. |
| [PdfPrimitiveType](Primitives/PdfPrimitiveType.md) | enum | Identifies the concrete type of a `PdfPrimitive`. |
| [PdfReal](Primitives/PdfReal.md) | class | Represents a PDF real (floating-point) object. |
| [PdfReference](Primitives/PdfReference.md) | class | Represents a PDF indirect object reference, e.g. `12 0 R`. |
| [PdfStream](Primitives/PdfStream.md) | class | Represents a PDF stream object — a dictionary plus a binary byte payload. |
| [PdfString](Primitives/PdfString.md) | class | Represents a PDF string object. |
| [PdfToken](Primitives/PdfToken.md) | struct | A lightweight token produced by `PdfTokenizer`. |
| [PdfTokenType](Primitives/PdfTokenType.md) | enum | Identifies the type of a token produced by `PdfTokenizer`. |
| [PdfTokenizer](Primitives/PdfTokenizer.md) | class | A forward-only, byte-level tokenizer for PDF streams. |
| [PdfTokenizerException](Primitives/PdfTokenizerException.md) | class | Thrown when the `PdfTokenizer` encounters bytes that cannot form a valid PDF token. |

## Chuvadi.Pdf.Redaction

| Type | Kind | Description |
|---|---|---|
| [RedactionException](Redaction/RedactionException.md) | class | Thrown when a redaction operation fails. |
| [RedactionOptions](Redaction/RedactionOptions.md) | class | Top-level configuration for a redaction operation. |
| [RedactionRect](Redaction/RedactionRect.md) | class | One rectangle of content to permanently remove from a PDF page. |
| [Redactor](Redaction/Redactor.md) | class | Applies true PHI-safe redactions to a PDF document. |

## Chuvadi.Pdf.Rendering

| Type | Kind | Description |
|---|---|---|
| [PageRasterizer](Rendering/PageRasterizer.md) | class | Rasterizes a PDF page to a `PixelBuffer`. |
| [RenderOptions](Rendering/RenderOptions.md) | class | Options that control how a PDF page is rasterized. |
| [RenderingException](Rendering/RenderingException.md) | class | Thrown when a PDF page cannot be rasterized due to an unsupported feature, invalid data, or internal rasterizer error. |
| [ScanlineRasterizer](Rendering/ScanlineRasterizer.md) | class | Fills vector paths into a `PixelBuffer` using a scanline edge-crossing algorithm. |
| [StrokeExpander](Rendering/StrokeExpander.md) | class | Converts a stroked path into a filled path by expanding each segment by half the stroke width on each side. |

## Chuvadi.Pdf.Text

| Type | Kind | Description |
|---|---|---|
| [ExtractionStrategy](Text/ExtractionStrategy.md) | enum | Specifies the text extraction strategy. |
| [LayoutExtractor](Text/LayoutExtractor.md) | class | Extracts text from `TextFragment` objects by reconstructing the visual reading order: group by line (Y position), sort by column (X position). |
| [OperatorExtractor](Text/OperatorExtractor.md) | class | Extracts text from a list of `TextFragment` objects in content stream order with simple heuristics for word and line breaks. |
| [TextExtractor](Text/TextExtractor.md) | class | Extracts plain text from a PDF page. |

## Chuvadi.Pdf.Watermark

| Type | Kind | Description |
|---|---|---|
| [ImageWatermarkOptions](Watermark/ImageWatermarkOptions.md) | class | Options for stamping an image watermark onto PDF pages. |
| [TextWatermarkOptions](Watermark/TextWatermarkOptions.md) | class | Options for stamping a text watermark onto PDF pages. |
| [WatermarkException](Watermark/WatermarkException.md) | class | Thrown when a watermark cannot be applied. |
| [WatermarkStamper](Watermark/WatermarkStamper.md) | class | Stamps text or image watermarks onto PDF pages by appending new content streams, preserving the original page content. |
