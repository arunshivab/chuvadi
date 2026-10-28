using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>
/// What ACTIVE or REMOTE content a document carries — macros, ActiveX controls, embedded
/// objects, DDE fields, and references to resources outside the file. Chuvadi.Docs only
/// DETECTS these; it never executes, loads, instantiates or fetches any of them. Intended
/// for mail clients, viewers and upload gateways that must warn about, quarantine or strip
/// such documents.
///
/// <code>
/// using var r = DocxReader.Open(attachment);
/// if (r.HasMacros) ShowMacroWarning();
/// var active = r.GetActiveContent();
/// if (active.HasRemoteTemplate || active.HasDdeFields) Quarantine();
/// </code>
/// </summary>
public sealed class DocxActiveContent
{
    /// <summary>
    /// The main document part has a macro-enabled content type (a .docm or .dotm package).
    /// Word treats such a file as able to run macros, whatever its file extension.
    /// </summary>
    public bool IsMacroEnabledPackage { get; init; }

    /// <summary>The package contains a VBA project part (<c>vbaProject.bin</c>).</summary>
    public bool ContainsVbaProject { get; init; }

    /// <summary>True when <see cref="IsMacroEnabledPackage"/> or <see cref="ContainsVbaProject"/>.</summary>
    public bool HasMacros => IsMacroEnabledPackage || ContainsVbaProject;

    /// <summary>The package contains ActiveX control parts (<c>word/activeX/…</c>).</summary>
    public bool HasActiveXControls { get; init; }

    /// <summary>The package contains embedded OLE objects or packages (<c>word/embeddings/…</c>).</summary>
    public bool HasEmbeddedObjects { get; init; }

    /// <summary>
    /// The package contains alternative-format chunks (<c>w:altChunk</c> — embedded HTML, MHT,
    /// RTF or another docx that Word converts when opening).
    /// </summary>
    public bool HasAltChunks { get; init; }

    /// <summary>
    /// A field instruction uses DDE or DDEAUTO (dynamic data exchange), which can launch
    /// external programs when fields update. Scanned in the body, headers, footers,
    /// footnotes and endnotes.
    /// </summary>
    public bool HasDdeFields { get; init; }

    /// <summary>
    /// The document attaches a template by EXTERNAL reference (a URL or UNC path) — Word
    /// fetches it on open. A well-known remote-template injection vector.
    /// </summary>
    public bool HasRemoteTemplate { get; init; }

    /// <summary>
    /// The document references external resources that a consumer would FETCH to render it
    /// (linked images, frames, subdocuments, remote templates) — i.e. any external
    /// relationship other than a plain hyperlink. Fetching them can leak that the document
    /// was opened (tracking) or pull hostile content.
    /// </summary>
    public bool HasExternalResources { get; init; }

    /// <summary>
    /// Every relationship whose target is outside the package (TargetMode="External"),
    /// hyperlinks included, in the order they were found.
    /// </summary>
    public IReadOnlyList<DocxExternalReference> ExternalReferences { get; init; } = Array.Empty<DocxExternalReference>();

    /// <summary>True when any active or fetch-on-render content is present (hyperlinks alone do not count).</summary>
    public bool HasAnyActiveContent =>
        HasMacros || HasActiveXControls || HasEmbeddedObjects || HasAltChunks
        || HasDdeFields || HasRemoteTemplate || HasExternalResources;
}

/// <summary>One external relationship found in a document. Never dereferenced by Chuvadi.Docs.</summary>
/// <param name="SourcePart">The package part that owns the relationship, e.g. "/word/document.xml".</param>
/// <param name="RelationshipType">The full relationship type URI.</param>
/// <param name="Target">The external target exactly as written in the file (URL, UNC or file path).</param>
/// <param name="IsHyperlink">True for ordinary hyperlinks (navigated only when a user clicks).</param>
public readonly record struct DocxExternalReference(string SourcePart, string RelationshipType, string Target, bool IsHyperlink);
