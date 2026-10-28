using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Markup.Internal;
using Chuvadi.Markup.Markdown.Parsing;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>
    /// Parses Markdown (CommonMark 0.31.2, plus the GFM table, task list, strikethrough and extended
    /// autolink extensions when enabled) into a <see cref="MarkdownDocument"/>.
    /// </summary>
    /// <remarks>
    /// Safe for untrusted input: the parser is linear apart from bounded rescans, honours the
    /// <see cref="CancellationToken"/>, and enforces <see cref="MarkupLimits"/>
    /// (<see cref="MarkupLimitExceededException"/> for input length and node count; graceful degradation
    /// for nesting depth, table size and link definition count).
    /// </remarks>
    public static class MarkdownParser
    {
        /// <summary>Parses Markdown text.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is null.</exception>
        /// <exception cref="MarkupLimitExceededException">A hard limit was exceeded.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public static MarkdownDocument Parse(string markdown, MarkdownOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(markdown);
            options ??= new MarkdownOptions();
            var limits = options.Limits ?? throw new ArgumentException("Options.Limits must not be null.", nameof(options));
            limits.Validate();
            Guard.InputLength(markdown.Length, limits);
            cancellationToken.ThrowIfCancellationRequested();

            var budget = new NodeBudget(limits.MaxNodes);
            var poll = new CancellationPoll(cancellationToken);
            var doc = new MarkdownDocument { Source = markdown };
            var blocks = new BlockParser(options, budget, cancellationToken, doc.MutableDefinitions).Parse(markdown);
            var inline = new InlineParser(options, doc.MutableDefinitions, budget, poll, markdown.Length);
            AstBuilder.Build(blocks, doc, inline, options, poll);
            doc.Span = new SourceSpan(0, markdown.Length);
            return doc;
        }

        /// <summary>Reads all text from a reader (up to <see cref="MarkupLimits.MaxInputChars"/>) and parses it.</summary>
        public static MarkdownDocument Parse(TextReader reader, MarkdownOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            options ??= new MarkdownOptions();
            string text = TextInput.ReadAll(reader, options.Limits.MaxInputChars, cancellationToken);
            return Parse(text, options, cancellationToken);
        }
    }
}
