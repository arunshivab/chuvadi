using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Markup.Html;

namespace Chuvadi.Markup.Sanitization
{
    /// <summary>
    /// Allowlist HTML sanitizer. Input is parsed with the WHATWG algorithm (the tree a browser would build),
    /// cleaned according to a <see cref="SanitizerPolicy"/>, serialized, and then re-parsed and re-sanitized
    /// until the output is a fixed point, so what the web view parses is exactly what was sanitized.
    /// </summary>
    public static class HtmlSanitizer
    {
        /// <summary>Sanitizes an HTML document or fragment (mail bodies, web pages, rendered Markdown). Policy defaults to <see cref="SanitizerPolicy.Anjal"/>.</summary>
        /// <exception cref="MarkupLimitExceededException">A hard limit was exceeded.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public static SanitizeResult Sanitize(string html, SanitizerPolicy? policy = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            policy ??= SanitizerPolicy.Anjal();
            var doc = HtmlParser.Parse(html, ParserOptions(policy), cancellationToken);
            return Run(doc, policy, cancellationToken);
        }

        /// <summary>Sanitizes an HTML document given as bytes (charset sniffed as a browser does, or <see cref="HtmlParserOptions.Encoding"/>).</summary>
        public static SanitizeResult Sanitize(byte[] html, SanitizerPolicy? policy = null, System.Text.Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            policy ??= SanitizerPolicy.Anjal();
            var options = ParserOptions(policy);
            options.Encoding = encoding;
            var doc = HtmlParser.Parse(html, options, cancellationToken);
            return Run(doc, policy, cancellationToken);
        }

        /// <summary>Sanitizes a copy of an already parsed document (the input is not modified).</summary>
        public static SanitizeResult Sanitize(HtmlDocument document, SanitizerPolicy? policy = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            policy ??= SanitizerPolicy.Anjal();
            return Run((HtmlDocument)document.Clone(true), policy, cancellationToken);
        }

        private static HtmlParserOptions ParserOptions(SanitizerPolicy policy) => new HtmlParserOptions
        {
            Scripting = true,
            CollectErrors = false,
            Limits = policy.Limits,
        };

        private static SanitizeResult Run(HtmlDocument doc, SanitizerPolicy policy, CancellationToken ct)
        {
            policy.Limits.Validate();
            var actions = new List<SanitizerAction>();
            var blocked = new List<BlockedResource>();
            var trusted = new HashSet<string>(StringComparer.Ordinal);
            var warnings = new List<string>();
            string title = doc.Title;

            var engine = new SanitizerEngine(policy, false, trusted, ct, actions, blocked);
            var fragment = engine.RunDocument(doc);
            var serializerOptions = new HtmlSerializerOptions { Limits = policy.Limits, Scripting = true };
            string html = HtmlSerializer.Serialize(fragment, serializerOptions, ct);

            bool stable = false;
            int passes = 1;
            HtmlDocumentFragment current = fragment;
            for (int i = 0; i < policy.MaxStabilityPasses; i++)
            {
                ct.ThrowIfCancellationRequested();
                passes++;
                var reparsed = HtmlParser.ParseFragment(html, "body", HtmlNamespace.Html, ParserOptions(policy), ct);
                var verifier = new SanitizerEngine(policy, true, trusted, ct, new List<SanitizerAction>(), new List<BlockedResource>());
                verifier.RunFragment(reparsed);
                verifier.SanitizeStyleElementsInPlace(reparsed);
                string again = HtmlSerializer.Serialize(reparsed, serializerOptions, ct);
                current = reparsed;
                if (again == html)
                {
                    stable = true;
                    break;
                }
                html = again;
            }
            if (policy.MaxStabilityPasses == 0) stable = false;

            if (!stable && policy.MaxStabilityPasses > 0 && policy.UnstableOutput == UnstableOutputHandling.PlainText)
            {
                warnings.Add("Output did not stabilize after " + policy.MaxStabilityPasses + " passes; returned as plain text.");
                current = PlainTextFragment(current.TextContent, policy);
                html = HtmlSerializer.Serialize(current, serializerOptions, ct);
            }
            else if (!stable && policy.MaxStabilityPasses > 0)
            {
                warnings.Add("Output did not stabilize after " + policy.MaxStabilityPasses + " passes.");
            }
            return new SanitizeResult(html, current, actions, engine.ActionCount, blocked, stable, passes, title, warnings);
        }

        private static HtmlDocumentFragment PlainTextFragment(string text, SanitizerPolicy policy)
        {
            var frag = new HtmlDocumentFragment();
            var pre = new HtmlElement("pre");
            pre.SetAttribute("style", "white-space: pre-wrap");
            pre.AppendChild(new HtmlText(text));
            if (SanitizerEngine.IsValidClass(policy.ScopeClass) && SanitizerEngine.IsValidClass(policy.WrapperElement))
            {
                var wrap = new HtmlElement(policy.WrapperElement.ToLowerInvariant());
                wrap.SetAttribute("class", policy.ScopeClass!);
                wrap.AppendChild(pre);
                frag.AppendChild(wrap);
            }
            else
            {
                frag.AppendChild(pre);
            }
            return frag;
        }
    }
}
