# Chuvadi.OpenType — trace log (D-74)

Every behaviour rule in the code carries a `// TRACE R-OT-nnnn` tag. Each entry below says what the
rule is, where it comes from, and which files carry the tag. The check in
`tests/Chuvadi.OpenType.Tests` (TraceLogTests) fails when a tag has no entry or an entry names a file
that does not carry its tag. The log is append-only: a changed rule gets a new entry that says which
entry it supersedes.

Source types:
- **spec**: a public specification, with its section. All read on 2026-11-01 (see sources.md).
- **decision**: a Chuvadi design decision where the specification leaves the choice open, with the reason.
  Decisions that change visible behaviour are listed for Arun's approval in the phase report.
- **technique**: ordinary engineering with no font-format content.

Short names used below:
- OTFF = OpenType 1.9.1, "The OpenType Font File", https://learn.microsoft.com/typography/opentype/spec/otff
- OT-cmap, OT-head, OT-hhea, OT-hmtx, OT-maxp, OT-name, OT-OS2, OT-post, OT-kern, OT-glyf, OT-loca, OT-CFF =
  the OpenType 1.9.1 chapter for that table, https://learn.microsoft.com/typography/opentype/spec/<table>
- TN5176 = Adobe Technical Note #5176, The Compact Font Format Specification, 4 Dec 2003
- TN5177 = Adobe Technical Note #5177, The Type 2 Charstring Format, 16 Mar 2000
- WOFF2 = W3C Recommendation, WOFF File Format 2.0, https://www.w3.org/TR/WOFF2/

## R-OT-0001
- Rule: All reads from font data are bounds-checked. A read outside the data returns 0 (or an empty slice) instead of throwing, so damaged or truncated tables read as empty data.
- Source: decision. CONTRACT.md "Bad input must not crash or hang"; design note v1 §(a) "malformed parts read as empty". Big-endian byte order: OTFF "Data Types".
- Code: src/Chuvadi.OpenType/Internal/BigEndian.cs

## R-OT-0002
- Rule: A tag is four bytes; a shorter tag text is padded with spaces; only printable ASCII U+0020–U+007E is allowed.
- Source: spec. OTFF "Data Types", Tag: "Array of four uint8s (length = 32 bits) used to identify a table, design-variation axis, script, language system, feature, or baseline", with the space-padding convention noted for 'cvt '; WOFF2 §4.1 note on padded tags.
- Code: src/Chuvadi.OpenType/Tag.cs

## R-OT-0003
- Rule: The first four bytes identify the container: 0x00010000, 'OTTO' or 'true' = one sfnt font; 'ttcf' = a collection. 'wOFF' and 'wOF2' are recognised and refused with a clear message in this version. Anything else is not a font.
- Source: spec. OTFF "Table Directory" (sfntVersion 0x00010000 or 'OTTO'; 'true' noted as used by Apple), OTFF "Font Collections"; WOFF2 §3.2 signature. Refusing WOFF: decision pending Arun's approval of WOFF support (design note v1, [APPROVE]).
- Superseded in part: WOFF and WOFF2 are decoded on load since 2026-11-05 (R-OT-0036, D-155).
- Code: src/Chuvadi.OpenType/FontCollection.cs

## R-OT-0004
- Rule: The table directory is numTables at offset 4, then 16-byte table records (tag, checksum, offset, length) from offset 12. Offsets are from the start of the file (also for a face of a collection). A record whose table lies outside the data, or repeats an earlier tag, is left out; checksums are not verified.
- Source: spec. OTFF "Table Directory" (TableDirectory and TableRecord layouts); OTFF "Font Collections" (offsets from the beginning of the TTC file). Leaving out bad or duplicate records: decision (R-OT-0001).
- Code: src/Chuvadi.OpenType/OpenTypeFont.cs

## R-OT-0005
- Rule: A 'ttcf' header has version (uint32), numFonts (uint32) and numFonts Offset32 values to each font's table directory. Versions 1.0 and 2.0 are read the same way (the 2.0 DSIG fields are not used).
- Source: spec. OTFF "Font Collections", "TTC Header".
- Code: src/Chuvadi.OpenType/FontCollection.cs

## R-OT-0006
- Rule: 'head' is read at its fixed field offsets (majorVersion 0 … glyphDataFormat 52; 54 bytes). A font without a 54-byte 'head', or with unitsPerEm 0, cannot be used and loading throws. magicNumber and checksumAdjustment are reported, not enforced. Dates are seconds since 1904-01-01 00:00 UTC.
- Source: spec. OT-head (field list and LONGDATETIME), OTFF "Data Types" (LONGDATETIME). Throwing only for a missing head / zero unitsPerEm: decision (values are unusable for scaling).
- Code: src/Chuvadi.OpenType/OpenTypeFont.cs, src/Chuvadi.OpenType/Tables/HeadTable.cs

## R-OT-0007
- Rule: 'maxp' version 0.5 has version and numGlyphs (6 bytes); version 1.0 (0x00010000, 32 bytes) adds the TrueType limits. A font without at least 6 bytes of 'maxp' cannot be used.
- Source: spec. OT-maxp (version 0.5 and version 1.0 layouts).
- Code: src/Chuvadi.OpenType/OpenTypeFont.cs, src/Chuvadi.OpenType/Tables/MaxpHheaTables.cs

## R-OT-0008
- Rule: Outline format: in an 'OTTO' font, 'CFF ' (else 'CFF2') decides; otherwise 'glyf' with 'loca' means TrueType; otherwise 'CFF ' or 'CFF2' if present; else none.
- Source: spec. OTFF "Table Directory" (sfntVersion 'OTTO' for CFF data, 0x00010000 for TrueType), OTFF "Tables Related to TrueType Outlines" and "Tables Related to CFF Outlines". Order when a font has both: decision.
- Code: src/Chuvadi.OpenType/OpenTypeFont.cs

## R-OT-0009
- Rule: Advance width of glyph g: hMetrics[min(g, numberOfHMetrics-1)].advanceWidth. Left side bearing: hMetrics[g].lsb for g < numberOfHMetrics, else leftSideBearings[g - numberOfHMetrics]. A glyph index outside 0..numGlyphs-1, or numberOfHMetrics = 0, gives 0.
- Source: spec. OT-hmtx ("the advance width value of the last record applies to all remaining glyph IDs"; leftSideBearings for glyph IDs >= numberOfHMetrics). Out-of-range glyph = 0: decision, to be checked against the FontFace oracle (design note v1 d.7).
- Code: src/Chuvadi.OpenType/OpenTypeFont.cs

## R-OT-0010
- Rule: 'hhea' is read at its fixed field offsets (36 bytes; numberOfHMetrics at 34). A shorter table is treated as absent.
- Source: spec. OT-hhea field list.
- Code: src/Chuvadi.OpenType/Tables/MaxpHheaTables.cs

## R-OT-0011
- Rule: 'OS/2' fields are read in the version 5 order; fields added in later versions are read only when the version and length include them (v0: 68 or 78 bytes, v1: 86, v2–4: 96, v5: 100).
- Source: spec. OT-OS2 "OS/2 Table Formats", versions 0 to 5 (checked field by field). The short 68-byte version 0 form: decision, read leniently.
- Code: src/Chuvadi.OpenType/Tables/Os2Table.cs

## R-OT-0012
- Rule: 'post' header: version, italicAngle (16.16 Fixed), underlinePosition, underlineThickness, isFixedPitch, four memory fields (32 bytes). Glyph names are not read in this version.
- Source: spec. OT-post "Header".
- Code: src/Chuvadi.OpenType/Tables/PostTable.cs

## R-OT-0013
- Rule: Mac OS Roman: bytes 0x00–0x7F map to the same code points; bytes 0x80–0xFF use the published Apple table.
- Source: spec/data. Unicode Consortium, MAPPINGS/VENDORS/APPLE/ROMAN.TXT (data file). Bytes 0x00–0x1F and 0x7F are not listed there; mapping them to themselves is a decision.
- Code: src/Chuvadi.OpenType/Internal/MacRoman.cs

## R-OT-0014
- Rule: 'name' versions 0 and 1: header (version, count, storageOffset), 12-byte name records, version 1 language-tag records after them. Strings: platform 0 and platform 3 (encodings 0, 1, 10) are UTF-16BE; (1, 0) is Mac Roman; others keep raw bytes only. GetName prefers (3, 1|10, 0x0409), then any Windows Unicode, then platform 0, then (1, 0, 0), then any decoded record.
- Source: spec. OT-name "Naming table header", "Name records", "Platform, encoding and language". Preference order: decision.
- Code: src/Chuvadi.OpenType/Tables/NameTable.cs

## R-OT-0015
- Rule: 'kern' version 0: header (version, nTables), subtables with (version, length, coverage); coverage bits 0–3 are horizontal, minimum, cross-stream, override; format in bits 8–15. Format 0 pairs are sorted by (left << 16 | right) and found by binary search. Format 2 is listed but its values are not read; Apple's version 1 table reads as having no subtables.
- Source: spec. OT-kern (KernHeader, subtable header, Format 0 "The KernPair records must be ordered…"; note that Apple's extensions are not supported in OpenType). Not reading format 2 values: decision (the text leaves the class-value arithmetic ambiguous; Windows supports only format 0 per OT-kern).
- Code: src/Chuvadi.OpenType/Tables/KernTable.cs

## R-OT-0016
- Rule: The Unicode map uses one subtable: among Unicode subtables (platform 0 with any encoding except 5; platform 3 with encoding 1 or 10) in a supported format, a 32-bit format (10, 12, 13) wins, then platform 3, then the higher encoding ID, then the earlier record. Without one: (3, 0) Symbol with code points looked up unchanged, then (1, 0) Mac Roman (language 0 first) with code points converted to Mac Roman bytes. The format 14 subtable is the one at (0, 5).
- Source: spec. OT-cmap "Encoding records and encodings" ("all other subtables are exclusive"; "the 32-bit encoding should be used by applications"; format 14 under platform 0 / encoding 5). The tie-break order and the Symbol/Mac Roman fallbacks: decision (the spec says an application "may choose … but should make this selection consistently"). No 0xF000 remapping for Symbol fonts: decision, listed for approval.
- Code: src/Chuvadi.OpenType/Tables/CharacterMap.cs

## R-OT-0017
- Rule: A glyph index read from 'cmap' that is not below the font's glyph count is treated as unmapped (0).
- Source: decision (R-OT-0001; such an index cannot name a glyph of the font). To be checked against the FontFace oracle.
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0018
- Rule: cmap format 0: glyphIdArray[256] of uint8 at offset 6.
- Source: spec. OT-cmap "Format 0: Byte encoding table".
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0019
- Rule: cmap format 2: subHeaderKeys[256] (value = subHeader index × 8) from offset 6, subHeaders of 8 bytes from offset 518. A byte whose key is 0 is a single-byte code through subHeader 0; otherwise it is the high byte of a two-byte code. The low byte must be in [firstCode, firstCode + entryCount); the glyph array entry is idRangeOffset bytes past the idRangeOffset field; a non-zero entry has idDelta added modulo 65536.
- Source: spec. OT-cmap "Format 2: High byte mapping through table".
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0020
- Rule: cmap format 4: segCountX2 at 6; endCode[] at 14; startCode[] after reservedPad; idDelta[], idRangeOffset[], glyphIdArray[]. Find the first segment whose endCode >= c; if startCode <= c: idRangeOffset 0 gives (c + idDelta) mod 65536, otherwise the glyph array entry at idRangeOffset + 2 × (c − startCode) bytes past the idRangeOffset word, and a non-zero entry gets idDelta added mod 65536.
- Source: spec. OT-cmap "Format 4: Segment mapping to delta values".
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0021
- Rule: cmap format 6: firstCode at 6, entryCount at 8, glyphIdArray from 10.
- Source: spec. OT-cmap "Format 6: Trimmed table mapping".
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0022
- Rule: cmap format 10: startCharCode (uint32) at 12, numChars at 16, glyph array from 20.
- Source: spec. OT-cmap "Format 10: Trimmed array".
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0023
- Rule: cmap formats 12 and 13: numGroups at 12; 12-byte groups (startCharCode, endCharCode, glyphID) from 16, sorted by start. Format 12 maps c to startGlyphID + (c − startCharCode); format 13 maps every code of the group to the same glyph. Groups are found by binary search.
- Source: spec. OT-cmap "Format 12: Segmented coverage" and "Format 13: Many-to-one range mappings".
- Code: src/Chuvadi.OpenType/Tables/CmapSubtable.cs

## R-OT-0024
- Rule: cmap format 14: numVarSelectorRecords at 6; 11-byte records (varSelector uint24, defaultUVSOffset, nonDefaultUVSOffset, offsets from the subtable start) sorted by selector. A sequence in the non-default table maps to its own glyph; one in the default ranges (startUnicodeValue, additionalCount) uses the base character's normal mapping; otherwise the sequence is not listed and the base mapping is used.
- Source: spec. OT-cmap "Format 14: Unicode variation sequences", "Default UVS table", "Non-Default UVS table".
- Code: src/Chuvadi.OpenType/Tables/CharacterMap.cs

## R-OT-0025
- Rule: TrueType glyph bounds are the xMin, yMin, xMax, yMax fields of the glyph header in 'glyf', at the 'loca' offset (short format: uint16 × 2; long: uint32). A glyph whose loca range is shorter than the 10-byte header has no outline.
- Source: spec. OT-glyf "Glyph headers"; OT-loca (short and long versions; "this means the glyph has no outline" when consecutive offsets are equal); OT-head indexToLocFormat.
- Code: src/Chuvadi.OpenType/Tables/GlyfBounds.cs

## R-OT-0026
- Rule: CFF data: Header (major 1, hdrSize), then Name INDEX, Top DICT INDEX, String INDEX, Global Subr INDEX, in that order. An INDEX is count (uint16), offSize, (count+1) offsets that are 1-based relative to the byte before the data; an empty INDEX is only the count.
- Source: spec. TN5176 §2 "Data Layout" (Table 1), §5 "INDEX Data" (Table 7), §6 "Header" (Table 8). OT-CFF (the 'CFF ' table holds CFF version 1 data).
- Code: src/Chuvadi.OpenType/Tables/CffOutlines.cs

## R-OT-0027
- Rule: A CID-keyed font (Top DICT has ROS, FDArray and FDSelect) takes each glyph's local Subrs from the Private DICT of its Font DICT, chosen by FDSelect format 0 (one byte per glyph) or format 3 (ranges with a sentinel). Otherwise local Subrs come from the Top DICT's Private DICT.
- Source: spec. TN5176 §18 "CID-keyed Fonts", §19 "FDSelect", Table 10 "CIDFont Operator Extensions".
- Code: src/Chuvadi.OpenType/Tables/CffOutlines.cs

## R-OT-0028
- Rule: DICT data: operands 32–246 (b0 − 139), 247–250, 251–254 (two-byte forms), 28 (int16), 29 (int32), 30 (real, nibbles 0–9 . E E- − end); operators 0–21, with 12 as an escape. Operators read: CharStrings (17), Private (18), CharstringType (12 6), ROS (12 30), FDArray (12 36), FDSelect (12 37), Subrs (19, relative to the Private DICT).
- Source: spec. TN5176 §4 "DICT Data" (Table 3 "Operand Encoding", Table 5 "Nibble Definitions"), Table 9 "Top DICT Operator Entries", Table 23 "Private DICT Operators".
- Code: src/Chuvadi.OpenType/Tables/CffOutlines.cs

## R-OT-0029
- Rule: CFF glyph bounds come from running the glyph's Type 2 charstring: numbers (32–254, 28, 255 = 16.16), hints (stem counting for hintmask/cntrmask mask bytes), the width rule for the first stack-clearing operator, all path operators including the four flex forms, subroutine calls with the count-dependent bias (107 / 1131 / 32768), and the arithmetic and storage operators. The box covers every drawn segment's end points, the start point of each drawn subpath, and the x and y extremes of each Bézier curve; it is rounded outwards (floor of minima, ceiling of maxima). Subroutine depth is limited to 10 and each glyph to 200,000 operations. 'random' returns a fixed 0.5 so results are deterministic.
- Source: spec. TN5177 §3.1 "Type 2 Charstring Organization" (width), §3.2 "Charstring Number Encoding", §4 "Path Construction Operators" (rmoveto … flex1), "Hint Operators", "Arithmetic Operators", "Storage Operators", "Conditional Operators" (and, or, not, eq, ifelse), §4.7 "Subroutine Operators" (bias), Appendix B "Implementation Limits". OT-hmtx: for CFF, xMin (= left side bearing) and xMax "can be obtained from the CFF / CFF2 rasterizer". Using the exact curve extremes and outward rounding: decision, to be checked against the FontFace oracle (design note v1 d.7).
- Code: src/Chuvadi.OpenType/Tables/CffOutlines.cs

## R-OT-0030
- Rule: endchar ends the charstring. Its deprecated accented-character form (four extra arguments, TN5177 Appendix C) is not composed; such a glyph's bounds come only from what it draws itself.
- Source: spec. TN5177 §4.1 endchar and Note 8 / Appendix C. Not composing: decision (deprecated form), listed as a known limitation.
- Code: src/Chuvadi.OpenType/Tables/CffOutlines.cs

## R-OT-0031
- Rule: WOFF2 decoder: header fields, table directory entries (flags with known-tag index 0–62 or 63 + explicit tag; transform version in bits 6–7; null transform is 0 except 3 for glyf/loca), UIntBase128 (no leading 0x80, at most 5 bytes, no overflow) and 255UInt16 (253 = word, 254 = +506, 255 = +253). The Brotli stream must decode to the sum of the table lengths. (Collections: see R-OT-0037.)
- Source: spec. WOFF2 §3.1 "255UInt16 Data Type", "UIntBase128 Data Type", §3.2 "WOFF2 Header", §4.1 "Table directory format" (Known Table Tags), §5 "Compressed data format". Brotli via the .NET BCL (System.IO.Compression.BrotliDecoder).
- Code: src/Chuvadi.OpenType/Woff/Woff2Decoder.cs
- Moved: from tools/Chuvadi.Fonts.TestSupport into the library on 2026-11-05 (D-155); the rule is unchanged.

## R-OT-0032
- Rule: WOFF2 transformed 'glyf' (version 0): the seven stream sizes, then nContour, nPoints (255UInt16 per contour), flag, glyph, composite, bbox (bitmap of 4 × floor((numGlyphs+31)/32) bytes, then int16 values) and instruction streams, plus the optional overlapSimpleBitmap. Empty, simple and composite glyphs are rebuilt as standard 'glyf' records (simple glyph coordinates written as int16 deltas), the bounding box computed from all points unless explicit, glyph records padded to 4 bytes, and 'loca' rebuilt in indexFormat.
- Source: spec. WOFF2 §5.1 "Transformed glyf table format", "Reconstruction of Glyph Records", "Decoding of Empty Glyphs", "Decoding of Simple Glyphs", "Decoding of Composite Glyphs", "Decoding of Bounding Boxes"; §5.3 "Transformed loca table format"; OT-glyf for the output record layout.
- Code: src/Chuvadi.OpenType/Woff/Woff2Decoder.cs
- Moved: from tools/Chuvadi.Fonts.TestSupport into the library on 2026-11-05 (D-155); the rule is unchanged.

## R-OT-0033
- Rule: WOFF2 point triplets: the flag's top bit set means off-curve; the low 7 bits index the 128-row triplet table (byte count, X/Y bits, delta X/Y, signs).
- Source: spec. WOFF2 §5.2 "Decoding of variable-length X and Y coordinates", Triplet Encoding table.
- Code: src/Chuvadi.OpenType/Woff/Woff2Decoder.cs
- Moved: from tools/Chuvadi.Fonts.TestSupport into the library on 2026-11-05 (D-155); the rule is unchanged.

## R-OT-0034
- Rule: WOFF2 transformed 'hmtx' (version 1): flags (bit 0: lsb[] omitted, bit 1: leftSideBearing[] omitted; other bits must be 0), advanceWidth[numberOfHMetrics], then the arrays present; omitted side bearings are the glyphs' xMin from the rebuilt 'glyf' (0 for empty glyphs).
- Source: spec. WOFF2 §5.4 "Transformed hmtx table format".
- Code: src/Chuvadi.OpenType/Woff/Woff2Decoder.cs
- Moved: from tools/Chuvadi.Fonts.TestSupport into the library on 2026-11-05 (D-155); the rule is unchanged.

## R-OT-0035
- Rule: `FaceProgram` of a collection face is a new sfnt: the face's sfntVersion; numTables, searchRange (16 × the largest power of two <= numTables), entrySelector (its log2) and rangeShift (numTables × 16 − searchRange); table records sorted by tag; each table copied unchanged and padded with zeros to a 4-byte boundary; each record's checksum the uint32 sum of the padded table; 'head' checkSumAdjustment set to 0 for the sums and then to 0xB1B0AFBA minus the whole font's sum. The face's 'DSIG' table is left out. For a plain sfnt, FaceProgram is the loaded bytes unchanged.
- Source: spec. OTFF "Table Directory" (TableDirectory fields and their formulas, "The records in the array must be sorted in ascending order by tag", 4-byte alignment) and "Calculating Checksums" (checksum algorithm, checkSumAdjustment = 0xB1B0AFBA − sum). Dropping DSIG: decision D-153 (Arun, 2026-11-05).
- Code: src/Chuvadi.OpenType/Internal/SfntWriter.cs

## R-OT-0036
- Rule: Data starting with 'wOFF' is WOFF 1.0 and is decoded on load; data starting with 'wOF2' is WOFF 2.0 (R-OT-0031 to R-OT-0034, R-OT-0037). WOFF 1.0: 44-byte header (signature, flavor, length, numTables, reserved, totalSfntSize, versions, metadata and private block offsets and lengths) and 20-byte table directory entries (tag, offset, compLength, origLength, origChecksum). The file is rejected when the reserved field is not zero, a block or table lies outside the file or overlaps another, compLength > origLength, totalSfntSize differs from 12 + 16 × numTables + the padded table lengths, or a zlib-compressed table does not inflate to exactly origLength. compLength = origLength means stored uncompressed. The sfnt is rebuilt with the tables in their file order, the directory sorted by tag with recomputed searchRange, entrySelector and rangeShift, and the stored checksums kept, so a well-formed input font comes back byte for byte. FontCollection.Kind reports Woff or Woff2; Data is the decoded font.
- Source: spec. WOFF 1.0 §4 "WOFF Header", §5 "Table Directory", §6 "Font Data Tables"; zlib via the .NET BCL (System.IO.Compression.ZLibStream). Supporting WOFF: D-155.
- Code: src/Chuvadi.OpenType/Woff/Woff1Decoder.cs, src/Chuvadi.OpenType/FontCollection.cs

## R-OT-0037
- Rule: A WOFF2 file whose flavor is 'ttcf' carries a collection directory after the table directory: version (uint32), numFonts (255UInt16), then per font numTables (255UInt16), flavor (uint32) and numTables table indexes (255UInt16) into the table directory. Tables are decoded once and shared; each font's glyf and loca must be a pair (loca right after glyf when transformed); a transformed hmtx uses that font's hhea, maxp and glyf. The output is a 'ttcf' version 1.0 collection with each table stored once, recomputed checksums, and each font's head.checkSumAdjustment computed over that font's own directory and tables.
- Source: spec. WOFF2 §4.2 "Collection directory format" (including "a decoder MUST either set the TTC Header fields {ulDsigTag, ulDsigLength, ulDsigOffset} … to null or convert the TTC header format to version 1"), §5.1 and §5.4; OTFF "Font Collections". Per-font checkSumAdjustment in a collection: decision (the specifications do not say how to compute it for shared tables).
- Code: src/Chuvadi.OpenType/Woff/Woff2Decoder.cs, src/Chuvadi.OpenType/Internal/SfntWriter.cs

## R-OT-0038
- Rule: A WOFF or WOFF2 file that declares more than 256 MiB of decoded font data is refused with OpenTypeFormatException before anything that size is allocated.
- Source: decision (hostile-input guard; the specifications set no limit). The limit is internal; making it an option is listed for Arun.
- Code: src/Chuvadi.OpenType/Woff/WoffLimits.cs, src/Chuvadi.OpenType/Woff/Woff1Decoder.cs, src/Chuvadi.OpenType/Woff/Woff2Decoder.cs

## R-OT-0039
- Rule: A transformed WOFF2 'glyf' is paired with the next transformed 'loca' entry in the table directory, which need not come right after it; each transformed loca belongs to exactly one glyf, and in a collection each font's glyf and loca must be that pair.
- Source: spec + decision. WOFF2 §4.1 requires encoders to put loca right after glyf and §5.3 requires loca's transformLength to be 0; the pack's LiPi Sans WOFF2 files have other tables between them (found by running the decoder on the pack fonts, 2026-11-05), so the decoder accepts the next transformed loca instead of rejecting the file.
- Code: src/Chuvadi.OpenType/Woff/Woff2Decoder.cs
