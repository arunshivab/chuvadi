using System;
using System.Collections.Generic;
using System.Xml;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// A light stand-in for <see cref="XmlReader.ReadSubtree"/> on the Load hot path (body, table, row,
/// cell, paragraph, run and property subtrees). It moves through the same nodes, reports the same
/// node values and relative depths, and leaves the outer reader at the same place when it is
/// disposed (on the subtree's end element, or on the element itself when it is empty), so the
/// loader's loops behave exactly as they did with the BCL reader.
///
/// The BCL subtree reader also keeps its own namespace scope: on each element it walks the attributes
/// and adds a synthetic <c>xmlns</c> attribute for every prefix declared outside the subtree. That
/// costs a namespace manager, arrays and an attribute walk per element and per nesting level, which
/// made <c>Document.Load</c> slow on large documents (D-98). This reader leaves those synthetic
/// attributes out and answers <see cref="LookupNamespace"/> from the outer reader. It is therefore
/// only used where nothing reads the subtree's namespace declarations: no attribute enumeration,
/// no <c>ReadOuterXml</c>/<c>ReadInnerXml</c>/<c>WriteNode</c>, no namespace lookup. The places that
/// capture XML verbatim (form-field data, drawing shape properties) still capture through a BCL
/// subtree reader of their own; their output was compared with 2.1.0's, including files built with
/// unusual namespace prefixes.
/// Reads still go through the outer (guarded) reader, so depth, element budget and cancellation
/// apply exactly as before. A well-formedness error met while closing is swallowed as the BCL reader
/// does; the outer reader is then in the Error state and the loader reports it.
/// </summary>
internal sealed class SubtreeReader : XmlReader, IXmlLineInfo, IXmlNamespaceResolver
{
    private enum State { Initial, Interactive, Error, EndOfFile, Closed }

    private readonly XmlReader _r;
    private readonly int _root;
    private State _state;
    private bool _empty; // positioned on the empty sentinel node (Initial, EndOfFile, Closed, or the inner reader ran out)

    private SubtreeReader(XmlReader reader)
    {
        _r = reader;
        _root = reader.Depth;
        _state = State.Initial;
        _empty = true;
    }

    /// <summary>The subtree of the element <paramref name="reader"/> is on, like <see cref="XmlReader.ReadSubtree"/>.</summary>
    public static XmlReader Open(XmlReader reader)
    {
        if (reader.NodeType != XmlNodeType.Element)
            throw new InvalidOperationException("ReadSubtree() can be called only if the reader is on an element node.");
        return new SubtreeReader(reader);
    }

    private bool Active => _state == State.Interactive;

    public override XmlNodeType NodeType => _empty ? XmlNodeType.None : _r.NodeType;
    public override string Name => _empty ? string.Empty : _r.Name;
    public override string LocalName => _empty ? string.Empty : _r.LocalName;
    public override string NamespaceURI => _empty ? string.Empty : _r.NamespaceURI;
    public override string Prefix => _empty ? string.Empty : _r.Prefix;
    public override string Value => _empty ? string.Empty : _r.Value;
    public override int Depth => _r.Depth - _root;
    public override string BaseURI => _r.BaseURI;
    public override bool IsEmptyElement => _r.IsEmptyElement;
    public override bool EOF => _state is State.EndOfFile or State.Closed;
    public override XmlNameTable NameTable => _r.NameTable;
    public override bool CanReadBinaryContent => _r.CanReadBinaryContent;
    public override bool CanReadValueChunk => _r.CanReadValueChunk;

    public override ReadState ReadState
    {
        get
        {
            if (_r.ReadState == ReadState.Error) return ReadState.Error;
            return _state switch
            {
                State.Initial => ReadState.Initial,
                State.Interactive => ReadState.Interactive,
                State.Error => ReadState.Error,
                State.EndOfFile => ReadState.EndOfFile,
                _ => ReadState.Closed,
            };
        }
    }

    public override int AttributeCount => Active ? _r.AttributeCount : 0;
    public override string? GetAttribute(string name) => Active ? _r.GetAttribute(name) : null;
    public override string? GetAttribute(string name, string? namespaceURI) => Active ? _r.GetAttribute(name, namespaceURI) : null;

    public override string GetAttribute(int i)
    {
        if (!Active) throw new ArgumentOutOfRangeException(nameof(i));
        return _r.GetAttribute(i);
    }

    public override bool MoveToAttribute(string name) => Active && _r.MoveToAttribute(name);
    public override bool MoveToAttribute(string name, string? ns) => Active && _r.MoveToAttribute(name, ns);

    public override void MoveToAttribute(int i)
    {
        if (!Active) throw new ArgumentOutOfRangeException(nameof(i));
        _r.MoveToAttribute(i);
    }

    public override bool MoveToFirstAttribute() => Active && _r.MoveToFirstAttribute();
    public override bool MoveToNextAttribute() => Active && _r.MoveToNextAttribute();
    public override bool MoveToElement()
    {
        if (!Active) return false;
        _empty = false; // as the BCL reader: back on the outer reader's node, even after a failed Read
        return _r.MoveToElement();
    }
    public override bool ReadAttributeValue() => Active && _r.ReadAttributeValue();
    public override string? LookupNamespace(string prefix) => Active ? _r.LookupNamespace(prefix) : null;
    public override void ResolveEntity() => _r.ResolveEntity();
    public override int ReadValueChunk(char[] buffer, int index, int count) => _r.ReadValueChunk(buffer, index, count);

    public override bool Read()
    {
        switch (_state)
        {
            case State.Initial:
                _empty = false;
                _state = State.Interactive;
                return true;
            case State.Interactive:
                _empty = false;
                _r.MoveToElement();
                if (_r.Depth == _root && (_r.NodeType == XmlNodeType.EndElement || (_r.NodeType == XmlNodeType.Element && _r.IsEmptyElement)))
                {
                    _state = State.EndOfFile;
                    _empty = true;
                    return false;
                }
                if (_r.Read()) return true;
                _empty = true;
                return false;
            default:
                return false;
        }
    }

    public override void Skip()
    {
        switch (_state)
        {
            case State.Initial:
                Read();
                return;
            case State.Interactive:
                _empty = false;
                _r.MoveToElement();
                if (_r.Depth == _root)
                {
                    if (_r.NodeType == XmlNodeType.Element && !_r.IsEmptyElement && _r.Read())
                        while (_r.NodeType != XmlNodeType.EndElement && _r.Depth > _root) _r.Skip();
                    _state = State.EndOfFile;
                    _empty = true;
                    return;
                }
                _r.Skip();
                return;
        }
    }

    /// <summary>
    /// As the BCL reader: a content read that fails leaves the subtree in the Error state (Read returns false,
    /// the node stays where the outer reader stopped); disposing still moves the outer reader on.
    /// </summary>
    public override string ReadContentAsString()
    {
        if (_state != State.Interactive) return string.Empty;
        _empty = false;
        // Delegated to the outer reader, as the BCL reader does, so a failure puts every subtree reader
        // in the chain into the Error state.
        try { return _r.ReadContentAsString(); }
        catch { _state = State.Error; throw; }
    }

    public override void Close()
    {
        if (_state == State.Closed) return;
        try
        {
            if (_state != State.EndOfFile)
            {
                _r.MoveToElement();
                if (_r.Depth == _root && _r.NodeType == XmlNodeType.Element && !_r.IsEmptyElement) _r.Read();
                while (_r.Depth > _root && _r.Read()) { }
            }
        }
        catch
        {
            // As the BCL subtree reader: the outer reader keeps the error (ReadState.Error) for its caller.
        }
        finally
        {
            _state = State.Closed;
            _empty = true;
        }
    }

    bool IXmlLineInfo.HasLineInfo() => _r is IXmlLineInfo li && li.HasLineInfo();
    // On the empty node (before the first Read, at the end, after Close) there is no position, as with the BCL reader.
    int IXmlLineInfo.LineNumber => !_empty && _r is IXmlLineInfo li ? li.LineNumber : 0;
    int IXmlLineInfo.LinePosition => !_empty && _r is IXmlLineInfo li ? li.LinePosition : 0;

    IDictionary<string, string> IXmlNamespaceResolver.GetNamespacesInScope(XmlNamespaceScope scope)
        => _r is IXmlNamespaceResolver r ? r.GetNamespacesInScope(scope) : new Dictionary<string, string>();

    string? IXmlNamespaceResolver.LookupNamespace(string prefix) => LookupNamespace(prefix);

    string? IXmlNamespaceResolver.LookupPrefix(string namespaceName)
        => _r is IXmlNamespaceResolver r ? r.LookupPrefix(namespaceName) : null;
}

internal static class SubtreeReaderExtensions
{
    /// <summary>The light <see cref="SubtreeReader"/> over the element the reader is on.</summary>
    public static XmlReader LightSubtree(this XmlReader reader) => SubtreeReader.Open(reader);
}
