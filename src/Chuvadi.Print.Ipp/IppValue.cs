using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Print.Ipp
{
    /// <summary>An IPP resolution (cross-feed x feed, with units).</summary>
    public readonly struct IppResolution : IEquatable<IppResolution>
    {
        public IppResolution(int crossFeed, int feed, IppResolutionUnits units = IppResolutionUnits.DotsPerInch)
        {
            CrossFeed = crossFeed;
            Feed = feed;
            Units = units;
        }

        public int CrossFeed { get; }
        public int Feed { get; }
        public IppResolutionUnits Units { get; }

        /// <summary>Cross-feed resolution in dots per inch, whatever the units.</summary>
        public double CrossFeedDpi => Units == IppResolutionUnits.DotsPerCentimetre ? CrossFeed * 2.54 : CrossFeed;

        /// <summary>Feed resolution in dots per inch, whatever the units.</summary>
        public double FeedDpi => Units == IppResolutionUnits.DotsPerCentimetre ? Feed * 2.54 : Feed;

        public bool Equals(IppResolution other) => CrossFeed == other.CrossFeed && Feed == other.Feed && Units == other.Units;
        public override bool Equals(object? obj) => obj is IppResolution r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(CrossFeed, Feed, Units);
        public override string ToString()
            => CrossFeed.ToString(CultureInfo.InvariantCulture) + "x" + Feed.ToString(CultureInfo.InvariantCulture)
               + (Units == IppResolutionUnits.DotsPerInch ? "dpi" : "dpcm");
    }

    /// <summary>An IPP rangeOfInteger (inclusive).</summary>
    public readonly struct IppRange : IEquatable<IppRange>
    {
        public IppRange(int lower, int upper)
        {
            Lower = lower;
            Upper = upper;
        }

        public int Lower { get; }
        public int Upper { get; }

        public bool Contains(int value) => value >= Lower && value <= Upper;
        public bool Equals(IppRange other) => Lower == other.Lower && Upper == other.Upper;
        public override bool Equals(object? obj) => obj is IppRange r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(Lower, Upper);
        public override string ToString() => Lower.ToString(CultureInfo.InvariantCulture) + "-" + Upper.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>An IPP textWithLanguage / nameWithLanguage value.</summary>
    public readonly struct IppStringWithLanguage : IEquatable<IppStringWithLanguage>
    {
        public IppStringWithLanguage(string language, string text)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string Language { get; }
        public string Text { get; }

        public bool Equals(IppStringWithLanguage other) => Language == other.Language && Text == other.Text;
        public override bool Equals(object? obj) => obj is IppStringWithLanguage s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(Language, Text);
        public override string ToString() => Text;
    }

    /// <summary>
    /// One IPP attribute value: its tag plus a typed payload. Unknown tags are kept as their raw bytes, so a
    /// decoded message re-encodes to the same bytes.
    /// </summary>
    public sealed class IppValue : IEquatable<IppValue>
    {
        private IppValue(IppTag tag, object? value, int extensionTag = 0)
        {
            Tag = tag;
            Value = value;
            ExtensionTag = extensionTag;
        }

        public IppTag Tag { get; }

        /// <summary>
        /// int (integer, enum), bool, string (text, name, keyword, uri, uriScheme, charset, naturalLanguage,
        /// mimeMediaType, memberAttrName), byte[] (octetString and unknown tags), DateTimeOffset, IppResolution,
        /// IppRange, IppStringWithLanguage, IppCollection, or null (out-of-band).
        /// </summary>
        public object? Value { get; }

        /// <summary>The 32-bit tag carried by an <see cref="IppTag.Extension"/> value.</summary>
        public int ExtensionTag { get; }

        public static IppValue Integer(int value) => new(IppTag.Integer, value);
        public static IppValue Boolean(bool value) => new(IppTag.Boolean, value);
        public static IppValue Enum(int value) => new(IppTag.Enum, value);
        public static IppValue Keyword(string value) => new(IppTag.Keyword, Required(value));
        public static IppValue Text(string value) => new(IppTag.TextWithoutLanguage, Required(value));
        public static IppValue Name(string value) => new(IppTag.NameWithoutLanguage, Required(value));
        public static IppValue TextWithLanguage(string language, string text) => new(IppTag.TextWithLanguage, new IppStringWithLanguage(language, text));
        public static IppValue NameWithLanguage(string language, string name) => new(IppTag.NameWithLanguage, new IppStringWithLanguage(language, name));
        public static IppValue Uri(string value) => new(IppTag.Uri, Required(value));
        public static IppValue UriScheme(string value) => new(IppTag.UriScheme, Required(value));
        public static IppValue Charset(string value) => new(IppTag.Charset, Required(value));
        public static IppValue NaturalLanguage(string value) => new(IppTag.NaturalLanguage, Required(value));
        public static IppValue MimeMediaType(string value) => new(IppTag.MimeMediaType, Required(value));
        public static IppValue OctetString(byte[] value) => new(IppTag.OctetString, (byte[])Required(value).Clone());
        public static IppValue DateTime(DateTimeOffset value) => new(IppTag.DateTime, value);
        public static IppValue Resolution(IppResolution value) => new(IppTag.Resolution, value);
        public static IppValue Resolution(int crossFeed, int feed, IppResolutionUnits units = IppResolutionUnits.DotsPerInch)
            => new(IppTag.Resolution, new IppResolution(crossFeed, feed, units));
        public static IppValue Range(int lower, int upper) => new(IppTag.RangeOfInteger, new IppRange(lower, upper));
        public static IppValue Collection(IppCollection value) => new(IppTag.BeginCollection, Required(value));

        /// <summary>An out-of-band value (unsupported, unknown, no-value, not-settable, delete-attribute, admin-define).</summary>
        public static IppValue OutOfBand(IppTag tag)
        {
            if ((byte)tag < 0x10 || (byte)tag > 0x1F) throw new ArgumentOutOfRangeException(nameof(tag), "Not an out-of-band tag.");
            return new IppValue(tag, null);
        }

        /// <summary>A value with a tag this library does not interpret, kept as raw bytes.</summary>
        public static IppValue Raw(IppTag tag, byte[] bytes, int extensionTag = 0) => new(tag, (byte[])Required(bytes).Clone(), extensionTag);

        internal static IppValue MemberName(string name) => new(IppTag.MemberAttrName, name);

        public bool IsOutOfBand => (byte)Tag >= 0x10 && (byte)Tag <= 0x1F;

        public int AsInteger() => Value is int i ? i : throw Mismatch("integer");
        public bool AsBoolean() => Value is bool b ? b : throw Mismatch("boolean");
        public string AsString() => Value switch
        {
            string s => s,
            IppStringWithLanguage w => w.Text,
            _ => throw Mismatch("string")
        };
        public IppResolution AsResolution() => Value is IppResolution r ? r : throw Mismatch("resolution");
        public IppRange AsRange() => Value is IppRange r ? r : throw Mismatch("rangeOfInteger");
        public IppCollection AsCollection() => Value is IppCollection c ? c : throw Mismatch("collection");
        public DateTimeOffset AsDateTime() => Value is DateTimeOffset d ? d : throw Mismatch("dateTime");
        public byte[] AsBytes() => Value is byte[] b ? (byte[])b.Clone() : throw Mismatch("octetString");

        public bool Equals(IppValue? other)
        {
            if (other is null || other.Tag != Tag || other.ExtensionTag != ExtensionTag) return false;
            return (Value, other.Value) switch
            {
                (null, null) => true,
                (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
                (IppCollection a, IppCollection b) => a.Equals(b),
                var (a, b) => Equals(a, b)
            };
        }

        public override bool Equals(object? obj) => obj is IppValue v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(Tag, Value is byte[] or IppCollection ? 0 : Value?.GetHashCode() ?? 0);

        public override string ToString() => Value switch
        {
            null => Tag.ToString(),
            byte[] b => "<" + b.Length.ToString(CultureInfo.InvariantCulture) + " bytes>",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Value.ToString() ?? string.Empty
        };

        private InvalidOperationException Mismatch(string wanted)
            => new("IPP value with tag " + Tag + " is not a " + wanted + ".");

        private static T Required<T>(T value) where T : class
            => value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>An IPP attribute: a name and one or more values (1setOf).</summary>
    public sealed class IppAttribute : IEquatable<IppAttribute>
    {
        private readonly List<IppValue> _values;

        public IppAttribute(string name, params IppValue[] values)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) throw new ArgumentException("An attribute needs at least one value.", nameof(values));
            _values = new List<IppValue>(values);
        }

        public IppAttribute(string name, IEnumerable<IppValue> values)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _values = new List<IppValue>(values ?? throw new ArgumentNullException(nameof(values)));
            if (_values.Count == 0) throw new ArgumentException("An attribute needs at least one value.", nameof(values));
        }

        public string Name { get; }
        public IReadOnlyList<IppValue> Values => _values;

        /// <summary>The first value.</summary>
        public IppValue Value => _values[0];

        public void Add(IppValue value) => _values.Add(value ?? throw new ArgumentNullException(nameof(value)));

        public bool Equals(IppAttribute? other)
        {
            if (other is null || other.Name != Name || other._values.Count != _values.Count) return false;
            for (int i = 0; i < _values.Count; i++) if (!_values[i].Equals(other._values[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is IppAttribute a && Equals(a);
        public override int GetHashCode() => HashCode.Combine(Name, _values.Count);
        public override string ToString() => Name + "=" + string.Join(",", _values);
    }

    /// <summary>An IPP collection value: an ordered set of member attributes.</summary>
    public sealed class IppCollection : IEquatable<IppCollection>
    {
        private readonly List<IppAttribute> _members = new();

        public IppCollection() { }

        public IppCollection(IEnumerable<IppAttribute> members)
        {
            foreach (IppAttribute m in members ?? throw new ArgumentNullException(nameof(members))) Add(m);
        }

        public IReadOnlyList<IppAttribute> Members => _members;

        public IppCollection Add(IppAttribute member)
        {
            _members.Add(member ?? throw new ArgumentNullException(nameof(member)));
            return this;
        }

        public IppCollection Add(string name, params IppValue[] values) => Add(new IppAttribute(name, values));

        public IppAttribute? this[string name]
        {
            get
            {
                foreach (IppAttribute m in _members) if (m.Name == name) return m;
                return null;
            }
        }

        public bool Equals(IppCollection? other)
        {
            if (other is null || other._members.Count != _members.Count) return false;
            for (int i = 0; i < _members.Count; i++) if (!_members[i].Equals(other._members[i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is IppCollection c && Equals(c);
        public override int GetHashCode() => _members.Count;
        public override string ToString() => "{" + string.Join(" ", _members) + "}";
    }

    /// <summary>A group of attributes introduced by a delimiter tag (operation, job, printer, ...).</summary>
    public sealed class IppAttributeGroup
    {
        private readonly List<IppAttribute> _attributes = new();

        public IppAttributeGroup(IppTag tag)
        {
            if ((byte)tag == 0 || (byte)tag > 0x0F || tag == IppTag.EndOfAttributes)
                throw new ArgumentOutOfRangeException(nameof(tag), "Not an attribute-group delimiter.");
            Tag = tag;
        }

        public IppTag Tag { get; }
        public IReadOnlyList<IppAttribute> Attributes => _attributes;

        public IppAttributeGroup Add(IppAttribute attribute)
        {
            _attributes.Add(attribute ?? throw new ArgumentNullException(nameof(attribute)));
            return this;
        }

        public IppAttributeGroup Add(string name, params IppValue[] values) => Add(new IppAttribute(name, values));

        /// <summary>Replaces the attribute with this name (keeping its position), or appends it.</summary>
        public IppAttributeGroup Set(string name, params IppValue[] values)
        {
            var attribute = new IppAttribute(name, values);
            int index = _attributes.FindIndex(a => a.Name == name);
            if (index < 0) _attributes.Add(attribute);
            else
            {
                _attributes[index] = attribute;
                _attributes.RemoveAll(a => a.Name == name && !ReferenceEquals(a, attribute));
            }
            return this;
        }

        /// <summary>Removes every attribute with this name. Returns true if any was removed.</summary>
        public bool Remove(string name) => _attributes.RemoveAll(a => a.Name == name) > 0;

        public IppAttribute? this[string name]
        {
            get
            {
                foreach (IppAttribute a in _attributes) if (a.Name == name) return a;
                return null;
            }
        }
    }
}
