using System;
using System.Collections;
using System.Collections.Generic;

namespace Chuvadi.Markup.Html
{
    /// <summary>Element namespace.</summary>
    public enum HtmlNamespace
    {
        /// <summary>http://www.w3.org/1999/xhtml</summary>
        Html,
        /// <summary>http://www.w3.org/2000/svg</summary>
        Svg,
        /// <summary>http://www.w3.org/1998/Math/MathML</summary>
        MathML,
    }

    /// <summary>Attribute namespace (only foreign elements carry namespaced attributes).</summary>
    public enum HtmlAttributeNamespace
    {
        /// <summary>No namespace.</summary>
        None,
        /// <summary>http://www.w3.org/1999/xlink</summary>
        XLink,
        /// <summary>http://www.w3.org/XML/1998/namespace</summary>
        Xml,
        /// <summary>http://www.w3.org/2000/xmlns/</summary>
        Xmlns,
    }

    /// <summary>An attribute.</summary>
    public sealed class HtmlAttribute
    {
        /// <summary>Creates an attribute with no namespace.</summary>
        public HtmlAttribute(string name, string value)
            : this(name, value, HtmlAttributeNamespace.None, null)
        {
        }

        /// <summary>Creates an attribute.</summary>
        public HtmlAttribute(string localName, string value, HtmlAttributeNamespace ns, string? prefix)
        {
            LocalName = localName ?? throw new ArgumentNullException(nameof(localName));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Namespace = ns;
            Prefix = prefix;
        }

        /// <summary>Local name (case as adjusted by the parser, e.g. <c>viewBox</c> on SVG).</summary>
        public string LocalName { get; }

        /// <summary>Prefix for namespaced attributes (<c>xlink</c>, <c>xml</c>, <c>xmlns</c>), or null.</summary>
        public string? Prefix { get; }

        /// <summary>Qualified name as it is serialized (<c>xlink:href</c>, <c>xmlns</c>, or the local name).</summary>
        public string Name => Prefix is null ? LocalName : Prefix + ":" + LocalName;

        /// <summary>Attribute namespace.</summary>
        public HtmlAttributeNamespace Namespace { get; }

        /// <summary>The value.</summary>
        public string Value { get; set; }

        /// <inheritdoc />
        public override string ToString() => Name + "=\"" + Value + "\"";
    }

    /// <summary>The ordered attribute list of an element; names are matched by qualified name, case-sensitively.</summary>
    public sealed class HtmlAttributeList : IReadOnlyList<HtmlAttribute>
    {
        private readonly List<HtmlAttribute> _list = new List<HtmlAttribute>();

        /// <inheritdoc />
        public int Count => _list.Count;

        /// <inheritdoc />
        public HtmlAttribute this[int index] => _list[index];

        /// <summary>Finds an attribute by qualified name.</summary>
        public HtmlAttribute? Find(string name)
        {
            foreach (var a in _list)
                if (a.Name == name) return a;
            return null;
        }

        /// <summary>Gets a value by qualified name, or null.</summary>
        public string? this[string name] => Find(name)?.Value;

        /// <summary>True if an attribute with the name exists.</summary>
        public bool Contains(string name) => Find(name) is not null;

        /// <summary>Sets (adds or replaces) a value.</summary>
        public void Set(string name, string value)
        {
            var a = Find(name);
            if (a is not null) a.Value = value;
            else _list.Add(new HtmlAttribute(name, value));
        }

        /// <summary>Adds an attribute object (replacing one with the same qualified name).</summary>
        public void Add(HtmlAttribute attribute)
        {
            ArgumentNullException.ThrowIfNull(attribute);
            for (int i = 0; i < _list.Count; i++)
            {
                if (_list[i].Name == attribute.Name)
                {
                    _list[i] = attribute;
                    return;
                }
            }
            _list.Add(attribute);
        }

        internal void AddUnchecked(HtmlAttribute attribute) => _list.Add(attribute);

        /// <summary>Removes by qualified name; returns true if removed.</summary>
        public bool Remove(string name)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                if (_list[i].Name == name)
                {
                    _list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes the attribute at an index.</summary>
        public void RemoveAt(int index) => _list.RemoveAt(index);

        /// <summary>Removes all attributes matching a predicate; returns the count removed.</summary>
        public int RemoveAll(Predicate<HtmlAttribute> match) => _list.RemoveAll(match);

        /// <summary>Removes all attributes.</summary>
        public void Clear() => _list.Clear();

        /// <inheritdoc />
        public IEnumerator<HtmlAttribute> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }

    /// <summary>An element.</summary>
    public sealed class HtmlElement : HtmlNode
    {
        /// <summary>Creates an element (detached).</summary>
        public HtmlElement(string localName, HtmlNamespace ns = HtmlNamespace.Html)
        {
            if (string.IsNullOrEmpty(localName)) throw new ArgumentException("A local name is required.", nameof(localName));
            LocalName = localName;
            Namespace = ns;
            if (ns == HtmlNamespace.Html && localName == "template") Content = new HtmlDocumentFragment();
        }

        /// <inheritdoc />
        public override HtmlNodeType NodeType => HtmlNodeType.Element;

        /// <inheritdoc />
        public override bool CanHaveChildren => true;

        /// <summary>Local name (lowercase for HTML; case-adjusted for SVG, e.g. <c>foreignObject</c>).</summary>
        public string LocalName { get; }

        /// <summary>Namespace.</summary>
        public HtmlNamespace Namespace { get; }

        /// <summary>Attributes in source order.</summary>
        public HtmlAttributeList Attributes { get; } = new HtmlAttributeList();

        /// <summary>For an HTML <c>template</c> element: its contents fragment; otherwise null.</summary>
        public HtmlDocumentFragment? Content { get; internal set; }

        /// <summary>True for an HTML element with the given local name.</summary>
        public bool IsHtml(string localName) => Namespace == HtmlNamespace.Html && LocalName == localName;

        /// <summary>Gets an attribute value, or null.</summary>
        public string? GetAttribute(string name) => Attributes[name];

        /// <summary>Sets an attribute value.</summary>
        public void SetAttribute(string name, string value) => Attributes.Set(name, value);

        /// <summary>Removes an attribute.</summary>
        public bool RemoveAttribute(string name) => Attributes.Remove(name);

        /// <summary>True if the attribute exists.</summary>
        public bool HasAttribute(string name) => Attributes.Contains(name);

        /// <summary>The <c>id</c> attribute, or empty.</summary>
        public string Id => GetAttribute("id") ?? string.Empty;

        /// <summary>Class names from the <c>class</c> attribute.</summary>
        public IReadOnlyList<string> ClassList => (GetAttribute("class") ?? string.Empty).Split(new[] { ' ', '\t', '\n', '\f', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        /// <inheritdoc />
        public override HtmlNode Clone(bool deep = true)
        {
            var e = new HtmlElement(LocalName, Namespace) { SourceOffset = SourceOffset };
            foreach (var a in Attributes) e.Attributes.AddUnchecked(new HtmlAttribute(a.LocalName, a.Value, a.Namespace, a.Prefix));
            if (deep)
            {
                CloneChildrenInto(e);
                if (Content is not null)
                {
                    e.Content = new HtmlDocumentFragment();
                    Content.CloneChildrenInto(e.Content);
                }
            }
            return e;
        }

        /// <inheritdoc />
        public override string ToString() => "<" + LocalName + ">";
    }
}
