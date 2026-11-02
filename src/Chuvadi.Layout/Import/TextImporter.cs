using System;
using System.Text;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Import
{
    /// <summary>How plain text is laid out.</summary>
    public sealed class TextImportOptions
    {
        /// <summary>Font family. Default "Courier New" (resolved to Liberation Mono by the bundled pack, metric-compatible).</summary>
        public string FontFamily { get; set; } = "Courier New";

        public double FontSize { get; set; } = 10;

        /// <summary>Page size and margins. Null (default) uses the paper of <see cref="LayoutOptions.Culture"/>'s region (D-30: Letter or A4) with 1" margins.</summary>
        public PageGeometry? Page { get; set; }

        /// <summary>Tab interval in characters of the font's '0' advance. Default 8.</summary>
        public int TabSizeInCharacters { get; set; } = 8;

        /// <summary>Encoding when the bytes have no byte-order mark. Null = UTF-8, falling back to Latin-1 for invalid UTF-8.</summary>
        public Encoding? Encoding { get; set; }

        /// <summary>Form feed (U+000C) starts a new page. Default true.</summary>
        public bool FormFeedIsPageBreak { get; set; } = true;
    }

    /// <summary>Plain text to <see cref="FlowDocument"/>: one paragraph per line, single spacing, no space between lines.</summary>
    public static class TextImporter
    {
        /// <summary>Decodes bytes: BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE) wins; otherwise the configured encoding, else UTF-8 with Latin-1 fallback.</summary>
        public static string Decode(ReadOnlySpan<byte> bytes, Encoding? encoding = null)
        {
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            {
                return new UTF32Encoding(false, false).GetString(bytes[4..]);
            }
            if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                return new UTF32Encoding(true, false).GetString(bytes[4..]);
            }
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes[3..]);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes[2..]);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes[2..]);
            }
            if (encoding is not null)
            {
                return encoding.GetString(bytes);
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        /// <summary>Builds the flow document. <paramref name="defaultTabStop"/> is in points.</summary>
        public static FlowDocument ToFlowDocument(string text, TextImportOptions options, double defaultTabStop)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);
            FlowDocument doc = new FlowDocument { DefaultTabStop = defaultTabStop };
            FlowSection section = new FlowSection { Page = options.Page ?? PageGeometry.A4 };
            doc.Sections.Add(section);
            CharacterFormat cf = new CharacterFormat
            {
                FontFamily = options.FontFamily,
                FontSize = options.FontSize,
                GenericFamily = Fonts.GenericFontFamily.Monospace,
            };
            ParagraphFormat pf = new ParagraphFormat { WidowControl = false };
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                bool end = i == text.Length;
                char c = end ? '\0' : text[i];
                if (!end && c != '\n' && c != '\r')
                {
                    continue;
                }
                string line = text.Substring(start, i - start);
                if (!options.FormFeedIsPageBreak)
                {
                    line = line.Replace('\f', ' ');
                }
                FlowParagraph p = new FlowParagraph { Format = pf, MarkFormat = cf };
                if (line.Length > 0)
                {
                    p.Inlines.Add(new TextRun(line, cf));
                }
                if (!(end && line.Length == 0 && section.Blocks.Count > 0))
                {
                    section.Blocks.Add(p);
                }
                if (!end && c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                start = i + 1;
            }
            return doc;
        }
    }
}
