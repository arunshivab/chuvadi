// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7 (Document structure), §9.6.2 (Standard fonts)
// PHASE: Phase 1.3 — Authoring module

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Top-level entry point for creating fresh PDF documents.
/// </summary>
/// <remarks>
/// <para>
/// Pages are added in order via <see cref="AddPage"/>. Each returns a
/// <see cref="PageBuilder"/> for drawing. Optional document-level header
/// and footer callbacks run for every page just before save, with the
/// final page number and total page count supplied.
/// </para>
/// <para>
/// Call <see cref="Save"/> or <see cref="ToByteArray"/> to emit the PDF bytes.
/// </para>
/// </remarks>
public sealed class PdfDocumentBuilder
{
    private readonly List<PageBuilder> _pages = new();
    private Action<PageBuilder, int, int>? _header;
    private Action<PageBuilder, int, int>? _footer;
    private string? _title;
    private string? _author;
    private string? _subject;

    private PdfDocumentBuilder() { }

    /// <summary>Creates a new empty document builder.</summary>
    public static PdfDocumentBuilder Create() => new();

    /// <summary>Sets the document's /Title metadata.</summary>
    public PdfDocumentBuilder SetTitle(string title) { _title = title; return this; }

    /// <summary>Sets the document's /Author metadata.</summary>
    public PdfDocumentBuilder SetAuthor(string author) { _author = author; return this; }

    /// <summary>Sets the document's /Subject metadata.</summary>
    public PdfDocumentBuilder SetSubject(string sub) { _subject = sub; return this; }

    /// <summary>
    /// Registers a page header callback. The callback receives the page,
    /// 1-based page number, and total page count; it should draw header content.
    /// </summary>
    public PdfDocumentBuilder SetHeader(Action<PageBuilder, int, int> draw)
    {
        _header = draw;
        return this;
    }

    /// <summary>Registers a page footer callback. Same shape as <see cref="SetHeader"/>.</summary>
    public PdfDocumentBuilder SetFooter(Action<PageBuilder, int, int> draw)
    {
        _footer = draw;
        return this;
    }

    /// <summary>Adds a page of the given size and returns its builder.</summary>
    public PageBuilder AddPage(PageSize size)
    {
        PageBuilder p = new(size);
        _pages.Add(p);
        return p;
    }

    /// <summary>Saves the document to a stream.</summary>
    public void Save(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = ToByteArray();
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Returns the document as a byte array.</summary>
    public byte[] ToByteArray()
    {
        if (_pages.Count == 0)
        {
            throw new InvalidOperationException("Document has no pages.");
        }

        // Apply header/footer to each page so total-page-count is known.
        int total = _pages.Count;
        for (int i = 0; i < total; i++)
        {
            _header?.Invoke(_pages[i], i + 1, total);
            _footer?.Invoke(_pages[i], i + 1, total);
        }

        return EmitPdf();
    }

    private byte[] EmitPdf()
    {
        // Object ID plan:
        // 1 = catalog, 2 = pages, 3..N = page objects, then content streams,
        // resource dicts, fonts, images, link annotations.
        List<PdfIndirectObject> objects = new();
        int nextId = 1;

        PdfObjectId catalogId = new(nextId++, 0);
        PdfObjectId pagesId = new(nextId++, 0);

        // Per-page IDs first; we need them for the /Kids array.
        PdfObjectId[] pageIds = new PdfObjectId[_pages.Count];
        for (int i = 0; i < _pages.Count; i++) { pageIds[i] = new(nextId++, 0); }

        // Per-page content stream + resources + annotations.
        for (int i = 0; i < _pages.Count; i++)
        {
            PageBuilder p = _pages[i];

            // Content stream
            PdfObjectId contentId = new(nextId++, 0);
            byte[] content = p.ContentStream();
            PdfDictionary contentDict = new();
            contentDict.Set(PdfName.Length, content.Length);
            objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));

            // Font dictionary entries — one per used font.
            PdfDictionary fontDict = new();
            foreach (string fontName in p.Fonts)
            {
                PdfObjectId fontId = new(nextId++, 0);
                PdfDictionary font = new();
                font.Set(PdfName.Type, PdfName.Intern("Font"));
                font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
                font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(fontName));
                font.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
                objects.Add(new PdfIndirectObject(fontId, font));
                fontDict.Set(PdfName.Intern(PageBuilder.FontKey(fontName)), new PdfReference(fontId));
            }

            // XObject dictionary for images.
            PdfDictionary xobjectDict = new();
            foreach (ImageRef img in p.Images)
            {
                PdfObjectId imgId = new(nextId++, 0);
                PdfStream imgStream = BuildImageStream(img.Bytes);
                objects.Add(new PdfIndirectObject(imgId, imgStream));
                xobjectDict.Set(PdfName.Intern(img.Key), new PdfReference(imgId));
            }

            // Resources
            PdfDictionary resources = new();
            if (p.Fonts.Count > 0) { resources.Set(PdfName.Intern("Font"), fontDict); }
            if (p.Images.Count > 0) { resources.Set(PdfName.Intern("XObject"), xobjectDict); }
            // Always declare ProcSet for older readers.
            PdfArray procSet = new();
            procSet.Add(PdfName.Intern("PDF"));
            procSet.Add(PdfName.Intern("Text"));
            if (p.Images.Count > 0)
            {
                procSet.Add(PdfName.Intern("ImageB"));
                procSet.Add(PdfName.Intern("ImageC"));
            }
            resources.Set(PdfName.Intern("ProcSet"), procSet);

            // Link annotations
            PdfArray annots = new();
            foreach (HyperlinkRect h in p.Hyperlinks)
            {
                PdfObjectId annotId = new(nextId++, 0);
                PdfDictionary annot = new();
                annot.Set(PdfName.Type, PdfName.Intern("Annot"));
                annot.Set(PdfName.Intern("Subtype"), PdfName.Intern("Link"));
                PdfArray rect = new();
                rect.Add(new PdfReal(h.XFromLeft));
                rect.Add(new PdfReal(h.YFromBottom));
                rect.Add(new PdfReal(h.XFromLeft + h.Width));
                rect.Add(new PdfReal(h.YFromBottom + h.Height));
                annot.Set(PdfName.Intern("Rect"), rect);
                PdfArray border = new();
                border.Add(new PdfInteger(0));
                border.Add(new PdfInteger(0));
                border.Add(new PdfInteger(0));
                annot.Set(PdfName.Intern("Border"), border);
                PdfDictionary action = new();
                action.Set(PdfName.Type, PdfName.Intern("Action"));
                action.Set(PdfName.Intern("S"), PdfName.Intern("URI"));
                action.Set(PdfName.Intern("URI"), new PdfString(h.LinkUri));
                annot.Set(PdfName.Intern("A"), action);
                objects.Add(new PdfIndirectObject(annotId, annot));
                annots.Add(new PdfReference(annotId));
            }

            // Page dictionary
            PdfDictionary page = new();
            page.Set(PdfName.Type, PdfName.Intern("Page"));
            page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
            PdfArray mediaBox = new();
            mediaBox.Add(new PdfReal(0));
            mediaBox.Add(new PdfReal(0));
            mediaBox.Add(new PdfReal(p.Width));
            mediaBox.Add(new PdfReal(p.Height));
            page.Set(PdfName.Intern("MediaBox"), mediaBox);
            page.Set(PdfName.Intern("Resources"), resources);
            page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
            if (p.Hyperlinks.Count > 0) { page.Set(PdfName.Intern("Annots"), annots); }
            objects.Add(new PdfIndirectObject(pageIds[i], page));
        }

        // Pages root
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Intern("Pages"));
        PdfArray kids = new();
        foreach (PdfObjectId id in pageIds) { kids.Add(new PdfReference(id)); }
        pagesDict.Set(PdfName.Intern("Kids"), kids);
        pagesDict.Set(PdfName.Intern("Count"), (PdfPrimitive)new PdfInteger(_pages.Count));
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        // Catalog
        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        // Info dictionary (optional)
        PdfDictionary trailer = new();
        trailer.Set(PdfName.Intern("Root"), new PdfReference(catalogId));
        if (_title is not null || _author is not null || _subject is not null)
        {
            PdfObjectId infoId = new(nextId++, 0);
            PdfDictionary info = new();
            if (_title is not null) { info.Set(PdfName.Intern("Title"), new PdfString(_title)); }
            if (_author is not null) { info.Set(PdfName.Intern("Author"), new PdfString(_author)); }
            if (_subject is not null) { info.Set(PdfName.Intern("Subject"), new PdfString(_subject)); }
            objects.Add(new PdfIndirectObject(infoId, info));
            trailer.Set(PdfName.Intern("Info"), new PdfReference(infoId));
        }

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    private static PdfStream BuildImageStream(byte[] imageBytes)
    {
        // Sniff JPEG vs PNG by magic bytes.
        if (imageBytes.Length >= 3 &&
            imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
        {
            return BuildJpegStream(imageBytes);
        }
        if (imageBytes.Length >= 8 &&
            imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
        {
            return BuildPngStream(imageBytes);
        }
        throw new ArgumentException("Image must be JPEG or PNG.", nameof(imageBytes));
    }

    private static PdfStream BuildJpegStream(byte[] jpegBytes)
    {
        // Parse JPEG SOF marker for dimensions.
        (int w, int h) = JpegSize(jpegBytes);
        PdfDictionary dict = new();
        dict.Set(PdfName.Type, PdfName.Intern("XObject"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), (PdfPrimitive)new PdfInteger(w));
        dict.Set(PdfName.Intern("Height"), (PdfPrimitive)new PdfInteger(h));
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        dict.Set(PdfName.Intern("BitsPerComponent"), (PdfPrimitive)new PdfInteger(8));
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("DCTDecode"));
        dict.Set(PdfName.Length, jpegBytes.Length);
        return new PdfStream(dict, jpegBytes);
    }

    private static PdfStream BuildPngStream(byte[] pngBytes)
    {
        // Parse PNG IHDR for dimensions, extract IDAT to embed.
        (int w, int h, byte[] data, bool hasAlpha) = PngExtract(pngBytes);
        PdfDictionary dict = new();
        dict.Set(PdfName.Type, PdfName.Intern("XObject"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), (PdfPrimitive)new PdfInteger(w));
        dict.Set(PdfName.Intern("Height"), (PdfPrimitive)new PdfInteger(h));
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern(hasAlpha ? "DeviceRGB" : "DeviceRGB"));
        dict.Set(PdfName.Intern("BitsPerComponent"), (PdfPrimitive)new PdfInteger(8));
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
        PdfDictionary decodeParms = new();
        decodeParms.Set(PdfName.Intern("Predictor"), (PdfPrimitive)new PdfInteger(15));
        decodeParms.Set(PdfName.Intern("Colors"), (PdfPrimitive)new PdfInteger(hasAlpha ? 4 : 3));
        decodeParms.Set(PdfName.Intern("BitsPerComponent"), (PdfPrimitive)new PdfInteger(8));
        decodeParms.Set(PdfName.Intern("Columns"), (PdfPrimitive)new PdfInteger(w));
        dict.Set(PdfName.Intern("DecodeParms"), decodeParms);
        dict.Set(PdfName.Length, data.Length);
        return new PdfStream(dict, data);
    }

    private static (int W, int H) JpegSize(byte[] bytes)
    {
        int i = 2;
        while (i < bytes.Length - 1)
        {
            if (bytes[i] != 0xFF) { i++; continue; }
            byte marker = bytes[i + 1];
            i += 2;
            // SOF0, SOF2, etc. — read height + width.
            if (marker >= 0xC0 && marker <= 0xC3)
            {
                int h = (bytes[i + 3] << 8) | bytes[i + 4];
                int w = (bytes[i + 5] << 8) | bytes[i + 6];
                return (w, h);
            }
            if (marker == 0xD8 || marker == 0xD9) { continue; }
            int len = (bytes[i] << 8) | bytes[i + 1];
            i += len;
        }
        throw new InvalidDataException("JPEG SOF marker not found.");
    }

    private static (int W, int H, byte[] Data, bool HasAlpha) PngExtract(byte[] bytes)
    {
        // PNG signature: 8 bytes. IHDR chunk starts at offset 8.
        int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        byte colorType = bytes[25];
        bool hasAlpha = colorType == 6;
        // Walk chunks; concatenate IDAT payloads.
        int p = 8;
        using MemoryStream idat = new();
        while (p < bytes.Length)
        {
            int len = (bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3];
            string type = System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4);
            if (type == "IDAT")
            {
                idat.Write(bytes, p + 8, len);
            }
            else if (type == "IEND") { break; }
            p += 12 + len;
        }
        return (w, h, idat.ToArray(), hasAlpha);
    }
}
