using System;
using System.Collections.Generic;

namespace Chuvadi.Markup.Sanitization
{
    /// <summary>What happens to an element the policy does not allow.</summary>
    public enum ElementAction
    {
        /// <summary>Keep the element (subject to attribute filtering).</summary>
        Keep,
        /// <summary>Remove the element but keep its children in its place.</summary>
        Unwrap,
        /// <summary>Remove the element and everything inside it.</summary>
        Drop,
    }

    /// <summary>How links (<c>a</c> and <c>area</c> with <c>href</c>) are emitted.</summary>
    public enum LinkHandling
    {
        /// <summary>Keep the (scheme-checked) href. The host is expected to intercept navigation.</summary>
        Keep,
        /// <summary>Remove the href so the link cannot navigate; the destination is kept in <see cref="SanitizerPolicy.InertLinkAttribute"/> and, if the element has none, a <c>title</c>.</summary>
        Inert,
        /// <summary>Replace the href with <see cref="SanitizerPolicy.LinkRewriteTemplate"/> (for a click-through page).</summary>
        Rewrite,
    }

    /// <summary>How <c>&lt;style&gt;</c> element contents are handled.</summary>
    public enum StyleSheetHandling
    {
        /// <summary>Remove style sheets.</summary>
        Drop,
        /// <summary>Sanitize style sheets and keep them unscoped (only when the output is shown in an isolated document).</summary>
        Sanitize,
        /// <summary>Sanitize style sheets and prefix every selector with <see cref="SanitizerPolicy.ScopeClass"/>; the output is wrapped in an element carrying that class.</summary>
        SanitizeScoped,
    }

    /// <summary>What to return when sanitized output does not reach a stable form after <see cref="SanitizerPolicy.MaxStabilityPasses"/>.</summary>
    public enum UnstableOutputHandling
    {
        /// <summary>Return the document's text, escaped, as the output (always safe). Default.</summary>
        PlainText,
        /// <summary>Return the output of the last pass.</summary>
        LastPass,
    }

    /// <summary>The kind of resource a URL refers to.</summary>
    public enum ResourceKind
    {
        /// <summary>An image (<c>img src</c>, <c>background</c> attributes, CSS images, SVG <c>image</c>).</summary>
        Image,
        /// <summary>Audio or video (<c>src</c>, <c>poster</c>).</summary>
        Media,
        /// <summary>A font (<c>@font-face src</c>).</summary>
        Font,
        /// <summary>A navigation target (<c>href</c>).</summary>
        Link,
        /// <summary>Another URL attribute (<c>cite</c>).</summary>
        Other,
    }

    /// <summary>A URL the sanitizer asks the host to resolve.</summary>
    public readonly record struct ResourceRequest(string Url, ResourceKind Kind, string ElementName, string? AttributeName);

    /// <summary>
    /// Maps URLs the policy would otherwise block (for example <c>cid:</c> parts of a mail, or files
    /// relative to a document) to a replacement URL, normally a <c>data:</c> URI. Return null to block.
    /// The replacement is trusted and emitted as given.
    /// </summary>
    public interface IResourceResolver
    {
        /// <summary>Returns a replacement URL, or null to block the resource.</summary>
        string? Resolve(ResourceRequest request);
    }

    /// <summary>An <see cref="IResourceResolver"/> backed by a delegate.</summary>
    public sealed class DelegateResourceResolver : IResourceResolver
    {
        private readonly Func<ResourceRequest, string?> _resolve;

        /// <summary>Creates the resolver.</summary>
        public DelegateResourceResolver(Func<ResourceRequest, string?> resolve) => _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));

        /// <inheritdoc />
        public string? Resolve(ResourceRequest request) => _resolve(request);
    }

    /// <summary>Kind of change the sanitizer made.</summary>
    public enum SanitizerActionKind
    {
        /// <summary>An element and its content were removed.</summary>
        RemovedElement,
        /// <summary>An element was removed and its children kept.</summary>
        UnwrappedElement,
        /// <summary>An attribute was removed.</summary>
        RemovedAttribute,
        /// <summary>A comment, DOCTYPE or processing instruction was removed.</summary>
        RemovedComment,
        /// <summary>A CSS declaration was removed.</summary>
        RemovedCssDeclaration,
        /// <summary>A CSS rule was removed.</summary>
        RemovedCssRule,
        /// <summary>A resource URL was blocked.</summary>
        BlockedResource,
        /// <summary>A resource URL was replaced by the resolver.</summary>
        ResolvedResource,
        /// <summary>A link was made inert or rewritten.</summary>
        NeutralizedLink,
        /// <summary>An attribute value was rewritten (id prefixing, forced types).</summary>
        RewroteAttribute,
    }

    /// <summary>One change made by the sanitizer.</summary>
    public sealed record SanitizerAction(SanitizerActionKind Kind, string Element, string? Attribute, string? Detail);

    /// <summary>A resource the sanitizer blocked.</summary>
    public sealed record BlockedResource(string Url, ResourceKind Kind, string Element, string? Attribute);

    /// <summary>The result of <see cref="HtmlSanitizer.Sanitize(string, SanitizerPolicy?, System.Threading.CancellationToken)"/>.</summary>
    public sealed class SanitizeResult
    {
        internal SanitizeResult(string html, Html.HtmlDocumentFragment fragment, List<SanitizerAction> actions, int actionCount,
            List<BlockedResource> blocked, bool stable, int passes, string title, List<string> warnings)
        {
            Html = html;
            Fragment = fragment;
            Actions = actions;
            ActionCount = actionCount;
            BlockedResources = blocked;
            IsStable = stable;
            Passes = passes;
            Title = title;
            Warnings = warnings;
        }

        /// <summary>The sanitized HTML fragment, ready to insert into a page (or a web view's body).</summary>
        public string Html { get; }

        /// <summary>The sanitized DOM (for layout or further processing). It re-serializes to <see cref="Html"/>.</summary>
        public Html.HtmlDocumentFragment Fragment { get; }

        /// <summary>Changes made (up to <see cref="MarkupLimits.MaxRecordedErrors"/>).</summary>
        public IReadOnlyList<SanitizerAction> Actions { get; }

        /// <summary>Total number of changes, including unrecorded ones.</summary>
        public int ActionCount { get; }

        /// <summary>Remote or unresolved resources that were blocked (deduplicated, in document order).</summary>
        public IReadOnlyList<BlockedResource> BlockedResources { get; }

        /// <summary>True when re-parsing and re-sanitizing the output reproduces it exactly (the output is a fixed point).</summary>
        public bool IsStable { get; }

        /// <summary>Number of sanitize passes run (1 plus stability passes).</summary>
        public int Passes { get; }

        /// <summary>The input document's title (from <c>&lt;title&gt;</c>), whitespace collapsed; empty if none.</summary>
        public string Title { get; }

        /// <summary>Notes such as the plain-text fallback being used.</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
