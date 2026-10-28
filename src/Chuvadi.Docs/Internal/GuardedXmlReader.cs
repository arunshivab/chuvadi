using System;
using System.IO;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// An <see cref="XmlReader"/> decorator that enforces a maximum element nesting depth and
/// observes a <see cref="CancellationToken"/> on every node. Every XML part Chuvadi.Docs
/// reads goes through this type (see <see cref="Create"/>), so the depth guard and the
/// cancellation check apply uniformly to the reader, the loader and the template engine.
///
/// The BCL's <see cref="XmlReaderSettings"/> has no depth limit; deep nesting is harmless to
/// our own (iterative) parsers but is a classic denial-of-service vector for recursive
/// consumers downstream (renderers, converters), so it is rejected at the source.
/// </summary>
internal sealed class GuardedXmlReader : XmlReader, IXmlLineInfo, IXmlNamespaceResolver
{
    private readonly XmlReader _inner;
    private readonly int? _maxDepth;
    private readonly CancellationToken _ct;
    private readonly string _partName;

    private GuardedXmlReader(XmlReader inner, int? maxDepth, CancellationToken ct, string partName)
    {
        _inner = inner;
        _maxDepth = maxDepth;
        _ct = ct;
        _partName = partName;
    }

    /// <summary>
    /// Creates a hardened reader over <paramref name="input"/>: DTDs prohibited, no external
    /// resolution, depth-limited, cancellable. The input stream is not closed by the reader.
    /// </summary>
    public static XmlReader Create(Stream input, int? maxDepth, CancellationToken ct, string partName,
        bool ignoreWhitespace = true, bool ignoreComments = true)
    {
        var inner = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = ignoreWhitespace,
            IgnoreComments = ignoreComments,
            CloseInput = false,
        });
        return new GuardedXmlReader(inner, maxDepth, ct, partName);
    }

    public override bool Read()
    {
        _ct.ThrowIfCancellationRequested();
        bool ok = _inner.Read();
        if (ok && _maxDepth is int max && _inner.Depth > max)
            throw new DocxSecurityLimitException(DocxLimit.XmlDepth, _inner.Depth, max, _partName);
        return ok;
    }

    // ---- Pure delegation --------------------------------------------------------------

    public override int AttributeCount => _inner.AttributeCount;
    public override string BaseURI => _inner.BaseURI;
    public override int Depth => _inner.Depth;
    public override bool EOF => _inner.EOF;
    public override bool IsEmptyElement => _inner.IsEmptyElement;
    public override string LocalName => _inner.LocalName;
    public override string Name => _inner.Name;
    public override string NamespaceURI => _inner.NamespaceURI;
    public override XmlNameTable NameTable => _inner.NameTable;
    public override XmlNodeType NodeType => _inner.NodeType;
    public override string Prefix => _inner.Prefix;
    public override ReadState ReadState => _inner.ReadState;
    public override string Value => _inner.Value;
    public override bool HasValue => _inner.HasValue;
    public override bool IsDefault => _inner.IsDefault;
    public override string XmlLang => _inner.XmlLang;
    public override XmlSpace XmlSpace => _inner.XmlSpace;
    public override XmlReaderSettings? Settings => _inner.Settings;
    public override string GetAttribute(int i) => _inner.GetAttribute(i);
    public override string? GetAttribute(string name) => _inner.GetAttribute(name);
    public override string? GetAttribute(string name, string? namespaceURI) => _inner.GetAttribute(name, namespaceURI);
    public override string? LookupNamespace(string prefix) => _inner.LookupNamespace(prefix);
    public override bool MoveToAttribute(string name) => _inner.MoveToAttribute(name);
    public override bool MoveToAttribute(string name, string? ns) => _inner.MoveToAttribute(name, ns);
    public override void MoveToAttribute(int i) => _inner.MoveToAttribute(i);
    public override bool MoveToElement() => _inner.MoveToElement();
    public override bool MoveToFirstAttribute() => _inner.MoveToFirstAttribute();
    public override bool MoveToNextAttribute() => _inner.MoveToNextAttribute();
    public override bool ReadAttributeValue() => _inner.ReadAttributeValue();
    public override void ResolveEntity() => _inner.ResolveEntity();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    bool IXmlLineInfo.HasLineInfo() => _inner is IXmlLineInfo li && li.HasLineInfo();
    int IXmlLineInfo.LineNumber => _inner is IXmlLineInfo li ? li.LineNumber : 0;
    int IXmlLineInfo.LinePosition => _inner is IXmlLineInfo li ? li.LinePosition : 0;

    System.Collections.Generic.IDictionary<string, string> IXmlNamespaceResolver.GetNamespacesInScope(XmlNamespaceScope scope)
        => _inner is IXmlNamespaceResolver r
            ? r.GetNamespacesInScope(scope)
            : new System.Collections.Generic.Dictionary<string, string>();

    string? IXmlNamespaceResolver.LookupNamespace(string prefix) => _inner.LookupNamespace(prefix);

    string? IXmlNamespaceResolver.LookupPrefix(string namespaceName)
        => _inner is IXmlNamespaceResolver r ? r.LookupPrefix(namespaceName) : null;
}
