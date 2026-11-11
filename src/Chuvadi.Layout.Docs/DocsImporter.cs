using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using D = Chuvadi.Docs.Model;

namespace Chuvadi.Layout.Docs
{
    /// <summary>Settings for <see cref="DocsImporter"/>.</summary>
    public sealed record DocsImportOptions
    {
        /// <summary>Lay out headers and footers. Default true.</summary>
        public bool IncludeHeadersAndFooters { get; init; } = true;

        /// <summary>Draw pictures. False skips them (their space is not reserved). Default true.</summary>
        public bool IncludeImages { get; init; } = true;

        /// <summary>Draw section watermarks. Default true.</summary>
        public bool IncludeWatermarks { get; init; } = true;

        /// <summary>Lay out hidden text (w:vanish), as Word does with "Print hidden text" on. Default false.</summary>
        public bool IncludeHiddenText { get; init; }

        /// <summary>
        /// Space before/after for paragraphs with "auto" spacing (w:beforeAutospacing / w:afterAutospacing, from HTML),
        /// in points. Default 14, as Word.
        /// </summary>
        public double AutoParagraphSpacing { get; init; } = 14;
    }

    /// <summary>
    /// Builds the flow IR from a .docx read by Chuvadi.Docs (1.6.0), the way Word lays it out. All formatting is the
    /// effective formatting Chuvadi.Docs resolves (document defaults, styles, table styles, numbering, direct
    /// formatting); theme colours, tints, shades and theme fonts are resolved against the document's theme.
    /// Imported: sections (page size, or the culture's paper when the section states none (D-30), margins, gutter, mirror margins, start type, page numbering), headers and footers
    /// with Word's linking rules, list labels, paragraphs (alignment, indents, spacing, line spacing, keep and widow
    /// settings, tabs and the document's default tab stop, borders, shading), runs (fonts per script, sizes, bold/italic per script, underline styles and
    /// colour, strikes, colour, highlight and shading, caps, super/subscript, spacing, scaling, kerning, baseline
    /// shift, hidden), breaks, tabs, positional tabs, PAGE/NUMPAGES/SECTIONPAGES fields, hyperlinks (with their targets), tables (grid, spans, legacy
    /// horizontal merges, vertical merges, resolved cell borders and shading, margins, row heights, header rows,
    /// cant-split rows, nested tables), pictures (the image runs of Chuvadi.Docs 6b: inline at their run, floating with
    /// alignment, percentage and point positions, relative sizes, crop, wrap type, side, polygon and distances) and text
    /// watermarks; and, since Chuvadi.Docs 1.6.0, footnotes (at the foot of the page of their reference) and endnotes (after the text),
    /// tracked changes laid out as accepted, symbols, inline and floating text boxes, floating tables at their horizontal position
    /// and the document's embedded fonts (<see cref="FlowDocument.EmbeddedFonts"/>). Markers, comment anchors and comment balloons take
    /// no room. Values are never altered.
    /// </summary>
    public static class DocsImporter
    {
        /// <summary>Reads a .docx (or an encrypted one, with <see cref="LayoutOptions.Password"/>) and builds the flow IR.</summary>
        /// <exception cref="LayoutFormatException">The data is not a readable document, or the password is missing or wrong.</exception>
        /// <exception cref="LayoutLimitExceededException">The document exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static FlowDocument Import(Stream source, DocsImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            layoutOptions ??= new LayoutOptions();
            D.Document doc = Load(source, layoutOptions, options ?? new DocsImportOptions(), cancellationToken);
            // The final view, as Word prints with changes accepted: insertions stay, deletions go, a deleted paragraph
            // mark joins its paragraph with the next. (The document was loaded here, so accepting changes alters no
            // caller's object; a Document passed in directly has deleted runs and rows skipped instead.)
            doc.AcceptAllRevisions();
            return Import(doc, options, layoutOptions, warning, cancellationToken);
        }

        /// <summary>Builds the flow IR from a document already loaded or built with Chuvadi.Docs.</summary>
        public static FlowDocument Import(D.Document document, DocsImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            return new DocsImport(document, options ?? new DocsImportOptions(), layoutOptions ?? new LayoutOptions(), warning ?? (_ => { }), cancellationToken).Run();
        }

        /// <summary>
        /// The Chuvadi.Docs reader limits for <paramref name="limits"/>: Docs' untrusted preset when the limits come from
        /// <see cref="LayoutLimits.ForUntrustedInput"/> (D-317 U1), with Layout's own limits on top; and an element cap of
        /// five per block (D-282 (b), D-317 E1).
        /// </summary>
        internal static D.DocxReaderOptions ReaderOptions(LayoutLimits limits, bool headersAndFooters)
        {
            D.DocxReaderOptions r = limits.FromUntrustedPreset ? D.DocxReaderOptions.ForUntrustedInput() : new D.DocxReaderOptions();
            r.MaxPackageBytes = limits.MaxSourceBytes;
            r.MaxPartBytes = limits.MaxSourceBytes;
            r.MaxTotalUncompressedBytes = limits.MaxLayoutBytes;
            r.MaxXmlDepth = Math.Max(64, limits.MaxNestingDepth * 16);
            r.MaxElements = ElementsFor(limits.MaxBlocks);
            r.IncludeHeadersAndFooters = headersAndFooters;
            return r;
        }

        /// <summary>The element cap for a block limit: five elements per block, within 1 and <see cref="int.MaxValue"/>.</summary>
        internal static int ElementsFor(long maxBlocks) => (int)Math.Clamp(maxBlocks * 5, 1, int.MaxValue);

        /// <summary>D-253: a missing or wrong password as <see cref="LayoutPasswordRequiredException"/>.</summary>
        internal static LayoutPasswordRequiredException PasswordError(string? password, Exception inner)
        {
            bool given = !string.IsNullOrEmpty(password);
            string message = given
                ? "The password for this document is wrong: " + inner.Message
                : "The document is encrypted: set LayoutOptions.Password. " + inner.Message;
            return new LayoutPasswordRequiredException(message, given, inner);
        }

        internal static D.Document Load(Stream source, LayoutOptions lo, DocsImportOptions o, CancellationToken ct)
        {
            try
            {
                return D.Document.Load(source, lo.Password, ReaderOptions(lo.Limits, o.IncludeHeadersAndFooters), ct);
            }
            catch (D.DocxPasswordRequiredException ex)
            {
                throw PasswordError(lo.Password, ex);
            }
            catch (D.DocxSecurityLimitException ex)
            {
                throw new LayoutLimitExceededException("Docx" + ex.Limit, ex.Allowed, ex.Actual);
            }
            catch (Exception ex) when (ex is D.DocxFormatException or InvalidDataException or System.Xml.XmlException or EndOfStreamException
                                       or NotSupportedException or FormatException)
            {
                throw new LayoutFormatException("The document could not be read: " + ex.Message, ex);
            }
        }
    }
}
