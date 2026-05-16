# API Reference

Auto-generated from XML doc comments. One file per public type, grouped by module.

Regenerate with:

```bash
python tools/gen_api_docs.py
```

## Chuvadi.

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

## Chuvadi.Cryptography

| Type | Kind | Description |
|---|---|---|
| [AccessDescription](Cryptography/AccessDescription.md) | class | One access description inside an AuthorityInfoAccess extension. |
| [AlgorithmIdentifier](Cryptography/AlgorithmIdentifier.md) | class | An ASN.1 AlgorithmIdentifier as defined by RFC 5280 §4.1.1.2. |
| [Asn1BitString](Cryptography/Asn1BitString.md) | class | Encode and decode ASN.1 BIT STRING values. |
| [Asn1Boolean](Cryptography/Asn1Boolean.md) | class | Encode and decode ASN.1 BOOLEAN values. |
| [Asn1Exception](Cryptography/Asn1Exception.md) | class | Raised when an ASN.1 decoder encounters malformed or non-conforming input. |
| [Asn1Integer](Cryptography/Asn1Integer.md) | class | Encode and decode ASN.1 INTEGER values. |
| [Asn1Null](Cryptography/Asn1Null.md) | class | Encode and decode ASN.1 NULL values. |
| [Asn1ObjectIdentifier](Cryptography/Asn1ObjectIdentifier.md) | class | Encode and decode ASN.1 OBJECT IDENTIFIER values. |
| [Asn1OctetString](Cryptography/Asn1OctetString.md) | class | Encode and decode ASN.1 OCTET STRING values. |
| [Asn1Reader](Cryptography/Asn1Reader.md) | class | Pull-style reader for nested ASN.1 BER/DER structures. |
| [Asn1String](Cryptography/Asn1String.md) | class | Encode and decode ASN.1 character string types. |
| [Asn1Tag](Cryptography/Asn1Tag.md) | struct | Immutable description of an ASN.1 tag. |
| [Asn1TagClass](Cryptography/Asn1TagClass.md) | enum | ASN.1 tag class. |
| [Asn1TagLength](Cryptography/Asn1TagLength.md) | class | Stateless low-level codec for ASN.1 BER/DER tag and length prefixes. |
| [Asn1Time](Cryptography/Asn1Time.md) | class | Encode and decode ASN.1 UTCTime and GeneralizedTime values. |
| [Asn1UniversalTag](Cryptography/Asn1UniversalTag.md) | enum | Universal-class ASN.1 tag numbers as assigned by ITU-T X.680. |
| [Asn1Writer](Cryptography/Asn1Writer.md) | class | Build-style writer for nested ASN.1 DER structures. |
| [AttributeTypeAndValue](Cryptography/AttributeTypeAndValue.md) | class | One attribute within a RelativeDistinguishedName — an OID identifying the attribute type plus its value. |
| [AuthorityInformationAccessExtension](Cryptography/AuthorityInformationAccessExtension.md) | class | The Authority Information Access extension — pointers to additional resources about the certificate's issuer (typically caIssuers and OCSP). |
| [AuthorityKeyIdentifierExtension](Cryptography/AuthorityKeyIdentifierExtension.md) | class | The Authority Key Identifier extension — identifies the public key whose holder signed this certificate. |
| [BasicConstraintsExtension](Cryptography/BasicConstraintsExtension.md) | class | The Basic Constraints extension — identifies CA certificates and bounds the depth of the chain they may issue. |
| [BitStringValue](Cryptography/BitStringValue.md) | class | A decoded ASN.1 BIT STRING — an octet sequence plus a count of unused trailing bits in the final octet. |
| [CmsAttribute](Cryptography/CmsAttribute.md) | class | A generic CMS Attribute — an OID identifying the attribute type, plus a SET of one or more values whose content is defined per OID. |
| [CmsAttributeTable](Cryptography/CmsAttributeTable.md) | class | A collection of `CmsAttribute` values, with OID lookup and the raw encoded bytes preserved for signature verification. |
| [CmsDecoder](Cryptography/CmsDecoder.md) | class | Decodes CMS / PKCS#7 byte streams into structured Chuvadi objects. |
| [ContentInfo](Cryptography/ContentInfo.md) | class | The outermost CMS structure — a tagged container that says "the following bytes are of contentType X." |
| [CrlDistributionPointsExtension](Cryptography/CrlDistributionPointsExtension.md) | class | The CRL Distribution Points extension — locations from which the issuer's Certificate Revocation List may be retrieved. |
| [DistributionPoint](Cryptography/DistributionPoint.md) | class | One distribution point inside a CRLDistributionPoints extension. |
| [EcCurve](Cryptography/EcCurve.md) | class | A named elliptic curve over a prime field — the parameters needed to perform ECDSA verification. |
| [EcPoint](Cryptography/EcPoint.md) | class | A point on a short Weierstrass elliptic curve in affine coordinates. |
| [EcdsaPublicKey](Cryptography/EcdsaPublicKey.md) | class | An ECDSA public key — a point on a named curve. |
| [EcdsaVerifier](Cryptography/EcdsaVerifier.md) | class | Verifies ECDSA signatures per FIPS 186-4 §6.4. |
| [EncapsulatedContentInfo](Cryptography/EncapsulatedContentInfo.md) | class | The content being signed (attached) or referenced (detached) by a SignedData. |
| [ExtendedKeyUsageExtension](Cryptography/ExtendedKeyUsageExtension.md) | class | The Extended Key Usage extension — additional or alternative purposes for which the certified public key may be used. |
| [GeneralName](Cryptography/GeneralName.md) | class | One alternative naming form for a certificate subject or other entity. |
| [GeneralNameKind](Cryptography/GeneralNameKind.md) | enum | The variant types within a GeneralName CHOICE. |
| [HashAlgorithmName](Cryptography/HashAlgorithmName.md) | enum | Enumeration of the hash algorithms Chuvadi implements. |
| [HashFactory](Cryptography/HashFactory.md) | class | Constructs hash algorithm instances by name or by OID. |
| [IHashAlgorithm](Cryptography/IHashAlgorithm.md) | interface | A streaming cryptographic hash function. |
| [IPublicKey](Cryptography/IPublicKey.md) | interface | Marker interface implemented by all Chuvadi public-key types. |
| [IssuerAndSerialNumber](Cryptography/IssuerAndSerialNumber.md) | class | Identifies an X.509 certificate by its issuer's distinguished name and the certificate's serial number. |
| [KeyUsageExtension](Cryptography/KeyUsageExtension.md) | class | The Key Usage extension — restricts the cryptographic operations the certified key may participate in. |
| [KeyUsageFlags](Cryptography/KeyUsageFlags.md) | enum | — |
| [KnownOids](Cryptography/KnownOids.md) | class | Named ObjectIdentifier constants for the OIDs Chuvadi cares about. |
| [ObjectIdentifier](Cryptography/ObjectIdentifier.md) | class | An ASN.1 OBJECT IDENTIFIER — an ordered sequence of non-negative arcs. |
| [OidNameLookup](Cryptography/OidNameLookup.md) | class | Maps an `ObjectIdentifier` to the friendly name from `KnownOids` for diagnostics and error messages. |
| [PublicKeyAlgorithm](Cryptography/PublicKeyAlgorithm.md) | enum | Public-key algorithm families Chuvadi recognises. |
| [RelativeDistinguishedName](Cryptography/RelativeDistinguishedName.md) | class | A SET of one or more attributes that together form one component of a DN. |
| [RsaPublicKey](Cryptography/RsaPublicKey.md) | class | An RSA public key — modulus n and public exponent e. |
| [RsaVerifier](Cryptography/RsaVerifier.md) | class | Verifies RSA signatures in PKCS#1 v1.5 (RSASSA-PKCS1-v1_5) and PSS (RSASSA-PSS) formats per RFC 8017. |
| [Sha256](Cryptography/Sha256.md) | class | SHA-256 hash function per FIPS 180-4 §6.2. |
| [Sha512](Cryptography/Sha512.md) | class | SHA-512 and SHA-384 hash functions per FIPS 180-4 §6.4 and §6.5. |
| [SignatureVerifier](Cryptography/SignatureVerifier.md) | class | Top-level signature-verification dispatcher. |
| [SignedData](Cryptography/SignedData.md) | class | A decoded CMS SignedData structure. |
| [SignerIdentifier](Cryptography/SignerIdentifier.md) | class | Identifies which certificate in the SignedData.certificates set produced a particular SignerInfo. |
| [SignerIdentifierKind](Cryptography/SignerIdentifierKind.md) | enum | The two variants of a SignerIdentifier. |
| [SignerInfo](Cryptography/SignerInfo.md) | class | One signer's contribution to a SignedData structure. |
| [SubjectAlternativeNameExtension](Cryptography/SubjectAlternativeNameExtension.md) | class | The Subject Alternative Name extension — additional naming forms for the certificate subject. |
| [SubjectKeyIdentifierExtension](Cryptography/SubjectKeyIdentifierExtension.md) | class | The Subject Key Identifier extension — a short octet string identifying the certificate's public key, used to find issuer certificates during path building. |
| [SubjectPublicKeyInfo](Cryptography/SubjectPublicKeyInfo.md) | class | The public key carried by an X.509 certificate, together with the algorithm identifier needed to interpret its bytes. |
| [TbsCertificate](Cryptography/TbsCertificate.md) | class | The "to-be-signed" body of an X.509 certificate. |
| [Validity](Cryptography/Validity.md) | class | The validity period of an X.509 certificate. |
| [X509Certificate](Cryptography/X509Certificate.md) | class | A fully-decoded X.509 certificate. |
| [X509Extension](Cryptography/X509Extension.md) | class | A single X.509 v3 extension — an OID, a criticality flag, and an opaque OCTET STRING value whose contents are defined per OID. |
| [X509Name](Cryptography/X509Name.md) | class | An X.500 distinguished name — a sequence of Relative Distinguished Names. |

## Chuvadi.Pdf.Documents

| Type | Kind | Description |
|---|---|---|
| [OptionalContentGroup](Documents/OptionalContentGroup.md) | class | An Optional Content Group (OCG) — a named, toggleable layer in a PDF. |
| [OptionalContentReader](Documents/OptionalContentReader.md) | class | Reads optional content groups (layers) from a PDF document. |
| [PdfDocument](Documents/PdfDocument.md) | class | Represents an opened PDF document. |
| [PdfDocumentException](Documents/PdfDocumentException.md) | class | Thrown when the PDF document model encounters an invalid or unsupported structure, such as a malformed page tree or a missing required entry. |
| [PdfPage](Documents/PdfPage.md) | class | Represents a single page in a PDF document. |
| [PdfPageCollection](Documents/PdfPageCollection.md) | class | Provides lazy, random-access to the pages of a PDF document. |
| [PdfRectangle](Documents/PdfRectangle.md) | struct | An immutable rectangle in PDF user space (points, 1/72 inch). |

## Chuvadi.Pdf.Encryption

| Type | Kind | Description |
|---|---|---|
| [AesCrypto](Encryption/AesCrypto.md) | class | AES-CBC encryption/decryption with PDF's IV-prefix wire format. |
| [Decryptor](Encryption/Decryptor.md) | class | Decrypts individual strings and streams in an encrypted PDF. |
| [EncryptionAlgorithm](Encryption/EncryptionAlgorithm.md) | enum | Identifies which encryption algorithm a PDF uses. |
| [EncryptionDictionary](Encryption/EncryptionDictionary.md) | class | Parsed view of a PDF's /Encrypt trailer entry. |
| [EncryptionException](Encryption/EncryptionException.md) | class | Thrown when a PDF encryption or decryption operation fails. |
| [Encryptor](Encryption/Encryptor.md) | class | Encrypts individual strings and streams for writing an encrypted PDF. |
| [PdfEncryption](Encryption/PdfEncryption.md) | class | Top-level helper for decrypting an encrypted PDF. |
| [Rc4](Encryption/Rc4.md) | class | RC4 stream cipher. |
| [StandardSecurityHandler](Encryption/StandardSecurityHandler.md) | class | Standard security handler: derives a file encryption key from a user/owner password and the document's /Encrypt dictionary. |

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
| [EncryptionOptions](IO/EncryptionOptions.md) | class | Options that drive encrypted PDF writing. |
| [LinearizationInfo](IO/LinearizationInfo.md) | class | Parsed view of a PDF's linearization parameter dictionary. |
| [LinearizationReader](IO/LinearizationReader.md) | class | Detects linearization and parses the parameter dictionary. |
| [PdfReader](IO/PdfReader.md) | class | Opens an existing PDF file and provides access to its object graph. |
| [PdfReaderException](IO/PdfReaderException.md) | class | Thrown when `PdfReader` encounters a PDF file structure it cannot parse or recover from. |
| [PdfWriter](IO/PdfWriter.md) | class | Writes a complete PDF file to an output stream. |

## Chuvadi.Pdf.Images

| Type | Kind | Description |
|---|---|---|
| [BmpEncoder](Images/BmpEncoder.md) | class | Encodes an `ImageFrame` to Windows BMP format. |
| [CmykConverter](Images/CmykConverter.md) | class | Converts `PixelBuffer` BGRA data to packed CMYK 8 bits per channel. |
| [ImageColorFormat](Images/ImageColorFormat.md) | enum | Specifies the colour format of a decoded image. |
| [ImageException](Images/ImageException.md) | class | Thrown when an image cannot be decoded or encoded due to an invalid format, unsupported feature, or data corruption. |
| [ImageFrame](Images/ImageFrame.md) | class | A decoded image frame held in a `PixelBuffer`. |
| [JpegDecoder](Images/JpegDecoder.md) | class | Decodes a baseline sequential DCT JPEG (SOF0) into an `ImageFrame`. |
| [PngDecoder](Images/PngDecoder.md) | class | Decodes a PNG image into an `ImageFrame`. |
| [PngEncoder](Images/PngEncoder.md) | class | Encodes an `ImageFrame` to PNG format. |
| [TiffDecoder](Images/TiffDecoder.md) | class | Decodes TIFF images per TIFF 6.0 baseline. |
| [TiffEncoder](Images/TiffEncoder.md) | class | Encodes one or more `ImageFrame` objects to a baseline TIFF 6.0 byte stream. |
| [TiffException](Images/TiffException.md) | class | Thrown when a TIFF operation fails. |

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
| [CommonPatterns](Redaction/CommonPatterns.md) | class | Pre-built regex strings for common PHI / PII tokens. |
| [PatternRule](Redaction/PatternRule.md) | class | A regex pattern that locates text to redact, with optional per-page filtering. |
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

## Chuvadi.Pdf.Signatures

| Type | Kind | Description |
|---|---|---|
| [ByteRange](Signatures/ByteRange.md) | class | The /ByteRange of a PDF signature — two disjoint regions of the file that together form the bytes the signature actually covers. |
| [PdfDocumentSignatureExtensions](Signatures/PdfDocumentSignatureExtensions.md) | class | Signature-related extensions on `PdfDocument`. |
| [PdfSignature](Signatures/PdfSignature.md) | class | One digital signature found in a PDF document. |
| [PdfSignatureVerifier](Signatures/PdfSignatureVerifier.md) | class | Orchestrates verification of a single `PdfSignature`. |
| [PdfSignatureVerifyExtensions](Signatures/PdfSignatureVerifyExtensions.md) | class | The user-visible `Verify()` entry point on `PdfSignature`. |
| [SignatureReader](Signatures/SignatureReader.md) | class | Reads digital-signature fields out of a PDF document's AcroForm tree. |
| [SignatureSubFilter](Signatures/SignatureSubFilter.md) | class | Constants and helpers for the /SubFilter entry of a PDF signature dictionary. |
| [SignatureVerificationResult](Signatures/SignatureVerificationResult.md) | class | The result of verifying a PDF digital signature. |
| [SignatureVerificationStatus](Signatures/SignatureVerificationStatus.md) | enum | The overall outcome of verifying a PDF signature. |
| [SignatureVerifyOptions](Signatures/SignatureVerifyOptions.md) | class | Options controlling signature verification. |

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
