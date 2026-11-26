using System.Collections.Generic;
using System.Xml;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// 2.2.1 (P1): an <see cref="XmlReader"/> decorator that refuses elements nested deeper than <see cref="MaxDepth"/>.
/// It wraps every reader whose part is loaded into an XLinq tree (styles, drawings, charts, and the sheet's extension
/// and other XML that is kept as elements), because .NET builds those trees in time that grows with the square of the
/// nesting depth: 40,000 nested elements take 7 s and 100,000 about two minutes, from a file of a few hundred KB
/// that passes every size limit. Real files nest a few dozen levels; 1,000 also leaves room for the 256 shape groups
/// a drawing may hold. Past it the part is malformed, exactly as for broken XML (an <see cref="XmlException"/>:
/// <see cref="Chuvadi.Sheets.Model.XlsxFormatException"/> from the readers, or a skipped optional part when
/// <see cref="Chuvadi.Sheets.Model.XlsxReaderOptions.StrictXml"/> is off). The parts read in one pass (sheet data,
/// shared strings, comments) are not affected by depth and are not wrapped.
/// </summary>
internal sealed class DepthLimitedXmlReader : XmlReader, IXmlLineInfo, IXmlNamespaceResolver
{
    internal const int MaxDepth = 1_000;

    private readonly XmlReader _inner;
    private readonly string _part;

    public DepthLimitedXmlReader(XmlReader inner, string part)
    {
        _inner = inner;
        _part = part;
    }

    public override bool Read()
    {
        bool ok = _inner.Read();
        if (ok && _inner.Depth > MaxDepth)
            throw new XmlException($"Part '{_part}': elements are nested deeper than {MaxDepth:N0} levels.");
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

    IDictionary<string, string> IXmlNamespaceResolver.GetNamespacesInScope(XmlNamespaceScope scope)
        => _inner is IXmlNamespaceResolver r ? r.GetNamespacesInScope(scope) : new Dictionary<string, string>();

    string? IXmlNamespaceResolver.LookupNamespace(string prefix) => _inner.LookupNamespace(prefix);

    string? IXmlNamespaceResolver.LookupPrefix(string namespaceName)
        => _inner is IXmlNamespaceResolver r ? r.LookupPrefix(namespaceName) : null;
}
