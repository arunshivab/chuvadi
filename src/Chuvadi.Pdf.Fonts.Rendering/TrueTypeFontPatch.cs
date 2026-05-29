// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — Table Directory, cmap (formats 4 and 12), head
//        https://docs.microsoft.com/typography/opentype/spec/otff
//        https://docs.microsoft.com/typography/opentype/spec/cmap
//        https://docs.microsoft.com/typography/opentype/spec/head
// PHASE: Phase 2.1 — v2.1.5 (cmap remap), v2.1.6 (extraction)
//        Rewrites a TrueType font program's cmap table so that glyph
//        indices reachable through legacy code points (e.g. Wingdings glyph
//        at code 0xFC) are ALSO reachable at the semantic Unicode code
//        points declared by a PDF ToUnicode CMap (e.g. U+2713 -> the same
//        glyph). Without this, browsers asked to render U+2713 fall back
//        to a generic checkmark from a system font instead of using the
//        embedded Wingdings glyph.
//
//        v2.1.6: cmap construction and SFNT serialisation were extracted to
//        CmapSubtableBuilder and SfntAssembler so the CFF-to-OpenType wrapper
//        can reuse them. This file now orchestrates those two helpers; the
//        emitted bytes are unchanged from v2.1.5.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Rewrites the cmap table of an embedded TrueType font program so the
/// browser can locate the embedded glyph by its semantic Unicode code
/// point rather than the font's legacy encoding code point.
/// </summary>
/// <remarks>
/// <para>
/// PDF symbol fonts (Wingdings, Symbol, Webdings, ZapfDingbats, MT Extra,
/// and similar) carry glyphs at legacy Windows-symbol code points — for
/// example, the Wingdings checkmark glyph is reachable at character code
/// 0xFC in the original Windows encoding. PDF readers do not use the
/// font's cmap; they address glyphs directly by glyph index (CID). When
/// such a PDF is exported to SVG and the embedded font is placed in a
/// <c>@font-face</c> rule, the browser DOES use the cmap table and asks
/// for the glyph at the semantic Unicode code point (U+2713 for the
/// checkmark). Since no entry exists in the font's cmap at U+2713, the
/// browser falls back to a system font and renders the wrong glyph.
/// </para>
/// <para>
/// The fix is to add a new cmap subtable mapping each semantic Unicode
/// code point (from the ToUnicode CMap) to the corresponding glyph index.
/// This implementation replaces the cmap table entirely with a fresh one
/// containing a single Windows Unicode subtable — format 4 (BMP only) or
/// format 12 (full Unicode range). The original cmap subtables are not
/// preserved; the embedded font is used only by browser rendering of the
/// SVG, where the new mappings are sufficient.
/// </para>
/// <para>
/// This approach mirrors pdf2htmlEX's font remapping strategy. The
/// alternative of mutating the original cmap in place would require
/// parsing every cmap subtable format the source font might use; replacing
/// the table is simpler and equally effective for the SVG use case.
/// </para>
/// </remarks>
public static class TrueTypeFontPatch
{
    /// <summary>
    /// Returns a copy of <paramref name="fontBytes"/> whose cmap table has
    /// been replaced by a fresh table containing the given Unicode-to-glyph
    /// mappings. The original cmap subtables are discarded.
    /// </summary>
    /// <param name="fontBytes">Original TrueType/OpenType font program bytes.</param>
    /// <param name="unicodeToGid">
    /// Map from Unicode code point to glyph index. Code points outside
    /// the BMP (>= 0x10000) trigger a format-12 cmap subtable; if all code
    /// points are in the BMP the result uses format 4.
    /// </param>
    /// <returns>New font program bytes with the cmap table replaced.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontBytes"/> or <paramref name="unicodeToGid"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// Thrown when the input bytes are not a valid TrueType/OpenType font
    /// or are too short to contain an offset table.
    /// </exception>
    public static byte[] WithAugmentedCmap(
        byte[] fontBytes,
        IReadOnlyDictionary<int, int> unicodeToGid)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(unicodeToGid);

        if (fontBytes.Length < 12)
        {
            throw new FontRenderingException(
                "Font data too short to contain an offset table.");
        }

        uint sfVersion = SfntAssembler.ReadUInt32BE(fontBytes, 0);
        if (sfVersion != 0x00010000 && sfVersion != 0x4F54544F
            && sfVersion != 0x74727565 && sfVersion != 0x74797031)
        {
            throw new FontRenderingException(
                $"Not a valid TrueType/OpenType font. sfVersion = 0x{sfVersion:X8}.");
        }

        int numTables = SfntAssembler.ReadUInt16BE(fontBytes, 4);
        if (12 + numTables * 16 > fontBytes.Length)
        {
            throw new FontRenderingException("Truncated table directory.");
        }

        // Collect existing tables, then replace cmap (or insert one if absent).
        List<SfntAssembler.TableEntry> tables = ReadTableDirectory(fontBytes, numTables);

        // Build the fresh cmap from the mappings (filtering and sorting happen
        // inside the builder).
        byte[] newCmap = CmapSubtableBuilder.BuildCmapTable(unicodeToGid);

        // Replace cmap entry; add it if missing.
        int cmapIdx = tables.FindIndex(t => t.Tag == 0x636D6170u);
        SfntAssembler.TableEntry cmapEntry = new(0x636D6170u, newCmap);
        if (cmapIdx >= 0)
        {
            tables[cmapIdx] = cmapEntry;
        }
        else
        {
            tables.Add(cmapEntry);
        }

        return SfntAssembler.Assemble(sfVersion, tables);
    }

    // ── Table directory ──────────────────────────────────────────────────

    private static List<SfntAssembler.TableEntry> ReadTableDirectory(byte[] data, int numTables)
    {
        List<SfntAssembler.TableEntry> tables = new(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            uint tag = SfntAssembler.ReadUInt32BE(data, entryOffset);
            uint offset = SfntAssembler.ReadUInt32BE(data, entryOffset + 8);
            uint length = SfntAssembler.ReadUInt32BE(data, entryOffset + 12);

            if (offset > data.Length || offset + length > data.Length)
            {
                throw new FontRenderingException(
                    $"Table {TagToString(tag)} extends past end of font.");
            }

            byte[] tableData = new byte[length];
            Array.Copy(data, (int)offset, tableData, 0, (int)length);
            tables.Add(new SfntAssembler.TableEntry(tag, tableData));
        }

        return tables;
    }

    private static string TagToString(uint tag)
    {
        return new string(new[]
        {
            (char)((tag >> 24) & 0xFF),
            (char)((tag >> 16) & 0xFF),
            (char)((tag >> 8) & 0xFF),
            (char)(tag & 0xFF),
        });
    }
}
