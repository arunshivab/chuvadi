using System.Collections.Generic;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// Reads link reference definitions from the start of a paragraph's raw content
    /// (CommonMark "Link reference definitions").
    /// </summary>
    internal static class LinkReferenceReader
    {
        /// <summary>
        /// Reads as many definitions as possible from the first lines of <paramref name="lines"/>, adds the
        /// new ones to the document (the first definition of a label wins) and returns how many lines they used.
        /// </summary>
        public static int Read(string source, List<ContentLine> lines, MarkdownDocument document, DefinitionBudget budget)
        {
            if (lines.Count == 0 || source[lines[0].Start] != '[') return 0;
            var content = Join(source, lines, out int[] textStarts);
            string text = content;
            int used = 0;
            int pos = 0;
            while (pos < text.Length && used < lines.Count)
            {
                // TRACE R-MD-0037
                if (lines[used].Indent > 3 || text[pos] != '[') break;
                if (!budget.CanAdd) break;
                int labelEnd = LinkSyntax.Label(text, pos, text.Length);
                // TRACE R-MD-0038
                if (labelEnd < 0 || labelEnd >= text.Length || text[labelEnd] != ':') break;
                int k = SkipSpaceAndOneLineEnding(text, labelEnd + 1);
                int destEnd = LinkSyntax.Destination(text, k, text.Length, out int destStart, out int destStop);
                if (destEnd < 0) break;
                // TRACE R-MD-0039
                if (text[k] != '<' && destStop == destStart) break;

                int defEnd = -1;
                string? title = null;
                // TRACE R-MD-0040
                int t = SkipSpaceAndOneLineEnding(text, destEnd);
                if (t > destEnd && t < text.Length)
                {
                    int titleEnd = LinkSyntax.Title(text, t, text.Length, out int ts, out int te);
                    if (titleEnd > 0)
                    {
                        int after = SkipSpacesAndTabs(text, titleEnd);
                        if (after >= text.Length || text[after] == '\n')
                        {
                            title = CharacterReferences.Resolve(text, ts, te);
                            defEnd = after;
                        }
                    }
                }
                // TRACE R-MD-0041
                if (defEnd < 0)
                {
                    int after = SkipSpacesAndTabs(text, destEnd);
                    if (after < text.Length && text[after] != '\n') break;
                    defEnd = after;
                }

                // the definition covers whole lines: find the line that holds defEnd
                int lastLine = LineOf(textStarts, used, defEnd);
                int nextLine = lastLine + 1;

                // TRACE R-MD-0108
                string label = text.Substring(pos + 1, labelEnd - 1 - (pos + 1));
                string destination = CharacterReferences.Resolve(text, destStart, destStop);
                string key = LinkReferenceDefinition.NormalizeLabel(label);
                var firstLine = lines[used];
                var endLine = lines[lastLine];
                // TRACE R-MD-0042
                // TRACE R-MD-0269
                // the span runs to where the next line's content enters: its first non-space char, or for a lazy line
                // where the container markers stopped (D-232, N3)
                int spanEnd = nextLine < lines.Count ? lines[nextLine].RawStart : endLine.Next;
                var span = new SourceSpan(firstLine.Start, spanEnd - firstLine.Start);
                // TRACE R-MD-0043
                if (!document.MutableDefinitions.ContainsKey(key))
                {
                    document.MutableDefinitions[key] = new LinkReferenceDefinition(CharacterReferences.Safe(label), destination, title) { Span = span };
                    budget.Added();
                }
                used = nextLine;
                pos = nextLine < textStarts.Length ? textStarts[nextLine] : text.Length;
            }
            return used;
        }

        // TRACE R-MD-0045
        private static int LineOf(int[] textStarts, int from, int index)
        {
            int line = from;
            while (line + 1 < textStarts.Length && textStarts[line + 1] <= index) line++;
            return line;
        }

        private static string Join(string source, List<ContentLine> lines, out int[] textStarts)
        {
            var sb = new System.Text.StringBuilder();
            textStarts = new int[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                textStarts[i] = sb.Length;
                sb.Append(source, lines[i].Start, lines[i].End - lines[i].Start);
            }
            return sb.ToString();
        }

        private static int SkipSpacesAndTabs(string s, int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            return i;
        }

        private static int SkipSpaceAndOneLineEnding(string s, int i)
        {
            i = SkipSpacesAndTabs(s, i);
            if (i < s.Length && s[i] == '\n') i = SkipSpacesAndTabs(s, i + 1);
            return i;
        }
    }

    // TRACE R-MD-0044
    /// <summary>Counts kept link reference definitions against <see cref="MarkupLimits.MaxLinkReferenceDefinitions"/>.</summary>
    internal sealed class DefinitionBudget
    {
        private readonly int _max;
        private int _count;

        public DefinitionBudget(int max) => _max = max;

        public bool CanAdd => _count < _max;

        public void Added() => _count++;
    }
}
