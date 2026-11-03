using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chuvadi.Print.Ipp
{
    /// <summary>Thrown when bytes are not a well-formed IPP message.</summary>
    public sealed class IppFormatException : Exception
    {
        public IppFormatException(string message) : base(message) { }
        public IppFormatException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// An IPP request or response (RFC 8010 binary encoding): version, operation or status code, request id
    /// and attribute groups. Any document data follows the message on the wire and is not part of it.
    /// </summary>
    public sealed class IppMessage
    {
        private readonly List<IppAttributeGroup> _groups = new();

        public IppMessage(short code, int requestId, byte versionMajor = 2, byte versionMinor = 0)
        {
            Code = code;
            RequestId = requestId;
            VersionMajor = versionMajor;
            VersionMinor = versionMinor;
        }

        public byte VersionMajor { get; set; }
        public byte VersionMinor { get; set; }

        /// <summary>The operation id (requests) or status code (responses).</summary>
        public short Code { get; set; }

        public int RequestId { get; set; }

        public IppOperation Operation => (IppOperation)Code;
        public IppStatusCode StatusCode => (IppStatusCode)Code;
        public bool IsSuccess => IppStatus.IsSuccess(Code);

        public IReadOnlyList<IppAttributeGroup> Groups => _groups;

        /// <summary>Appends a new group. Groups of the same tag may repeat (e.g. one job group per job in Get-Jobs).</summary>
        public IppAttributeGroup AddGroup(IppTag tag)
        {
            var group = new IppAttributeGroup(tag);
            _groups.Add(group);
            return group;
        }

        /// <summary>The first group with this tag, or null.</summary>
        public IppAttributeGroup? Group(IppTag tag)
        {
            foreach (IppAttributeGroup g in _groups) if (g.Tag == tag) return g;
            return null;
        }

        /// <summary>The first group with this tag, creating it if absent.</summary>
        public IppAttributeGroup GetOrAddGroup(IppTag tag) => Group(tag) ?? AddGroup(tag);

        /// <summary>Every group with this tag.</summary>
        public IEnumerable<IppAttributeGroup> GroupsOf(IppTag tag)
        {
            foreach (IppAttributeGroup g in _groups) if (g.Tag == tag) yield return g;
        }

        /// <summary>A new request with the operation attributes every IPP request starts with.</summary>
        public static IppMessage CreateRequest(IppOperation operation, int requestId, string printerUri,
                                               string? requestingUserName = null, string naturalLanguage = "en")
        {
            if (printerUri is null) throw new ArgumentNullException(nameof(printerUri));
            var message = new IppMessage((short)operation, requestId);
            IppAttributeGroup op = message.AddGroup(IppTag.OperationAttributes);
            op.Add("attributes-charset", IppValue.Charset("utf-8"));
            op.Add("attributes-natural-language", IppValue.NaturalLanguage(naturalLanguage));
            op.Add("printer-uri", IppValue.Uri(printerUri));
            if (!string.IsNullOrEmpty(requestingUserName))
                op.Add("requesting-user-name", IppValue.Name(requestingUserName));
            return message;
        }

        // ------------------------------------------------------------------ encoding

        public byte[] ToArray()
        {
            using var ms = new MemoryStream();
            WriteTo(ms);
            return ms.ToArray();
        }

        public void WriteTo(Stream output)
        {
            if (output is null) throw new ArgumentNullException(nameof(output));
            var w = new Writer(output);
            w.Byte(VersionMajor);
            w.Byte(VersionMinor);
            w.Int16(Code);
            w.Int32(RequestId);
            foreach (IppAttributeGroup group in _groups)
            {
                w.Byte((byte)group.Tag);
                foreach (IppAttribute attribute in group.Attributes) WriteAttribute(w, attribute.Name, attribute.Values);
            }
            w.Byte((byte)IppTag.EndOfAttributes);
        }

        private static void WriteAttribute(Writer w, string name, IReadOnlyList<IppValue> values)
        {
            for (int i = 0; i < values.Count; i++) WriteValue(w, i == 0 ? name : string.Empty, values[i]);
        }

        private static void WriteValue(Writer w, string name, IppValue value)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            if (value.Value is IppCollection collection)
            {
                w.Header(IppTag.BeginCollection, nameBytes);
                w.Length(0);
                foreach (IppAttribute member in collection.Members)
                {
                    w.Header(IppTag.MemberAttrName, Array.Empty<byte>());
                    w.Blob(Encoding.UTF8.GetBytes(member.Name));
                    foreach (IppValue memberValue in member.Values) WriteValue(w, string.Empty, memberValue);
                }
                w.Header(IppTag.EndCollection, Array.Empty<byte>());
                w.Length(0);
                return;
            }

            w.Header(value.Tag, nameBytes);
            switch (value.Value)
            {
                case null:
                    w.Length(0);
                    break;
                case int i when value.Tag is IppTag.Integer or IppTag.Enum:
                    w.Length(4);
                    w.Int32(i);
                    break;
                case bool b:
                    w.Length(1);
                    w.Byte(b ? (byte)1 : (byte)0);
                    break;
                case IppResolution r:
                    w.Length(9);
                    w.Int32(r.CrossFeed);
                    w.Int32(r.Feed);
                    w.Byte((byte)r.Units);
                    break;
                case IppRange r:
                    w.Length(8);
                    w.Int32(r.Lower);
                    w.Int32(r.Upper);
                    break;
                case DateTimeOffset d:
                    w.Length(11);
                    w.DateTime(d);
                    break;
                case IppStringWithLanguage s:
                {
                    byte[] lang = Encoding.ASCII.GetBytes(s.Language);
                    byte[] text = Encoding.UTF8.GetBytes(s.Text);
                    w.Length(4 + lang.Length + text.Length);
                    w.Blob(lang);
                    w.Blob(text);
                    break;
                }
                case string s:
                    w.Blob(IsUtf8(value.Tag) ? Encoding.UTF8.GetBytes(s) : Encoding.ASCII.GetBytes(s));
                    break;
                case byte[] raw when value.Tag == IppTag.Extension:
                    w.Length(4 + raw.Length);
                    w.Int32(value.ExtensionTag);
                    w.Raw(raw);
                    break;
                case byte[] raw:
                    w.Blob(raw);
                    break;
                default:
                    throw new InvalidOperationException("Cannot encode IPP value of type " + value.Value.GetType().Name + ".");
            }
        }

        private static bool IsUtf8(IppTag tag)
            => tag is IppTag.TextWithoutLanguage or IppTag.NameWithoutLanguage or IppTag.MemberAttrName;

        // ------------------------------------------------------------------ decoding

        /// <summary>Decodes a message from the start of <paramref name="data"/>.</summary>
        public static IppMessage Parse(ReadOnlySpan<byte> data) => Parse(data, out _);

        /// <summary>Decodes a message; <paramref name="length"/> is where any trailing document data begins.</summary>
        public static IppMessage Parse(ReadOnlySpan<byte> data, out int length)
        {
            var r = new Reader(data);
            var message = new IppMessage(0, 0, r.Byte(), r.Byte())
            {
                Code = r.Int16(),
                RequestId = r.Int32()
            };

            IppAttributeGroup? group = null;
            IppAttribute? current = null;
            while (true)
            {
                byte tag = r.Byte();
                if (tag == (byte)IppTag.EndOfAttributes) break;
                if (tag < 0x10)
                {
                    group = message.AddGroup((IppTag)tag);
                    current = null;
                    continue;
                }
                if (group is null) throw new IppFormatException("Attribute before any attribute group.");

                string name = r.String(r.Int16Length(), utf8: true);
                IppValue value = ReadValue(ref r, (IppTag)tag);
                if (name.Length == 0)
                {
                    if (current is null) throw new IppFormatException("Additional value without an attribute.");
                    current.Add(value);
                }
                else
                {
                    current = new IppAttribute(name, value);
                    group.Add(current);
                }
            }
            length = r.Position;
            return message;
        }

        /// <summary>
        /// Reads a whole stream (an IPP response, or a small request) and decodes the message;
        /// <paramref name="trailingData"/> receives any bytes after it (document data).
        /// </summary>
        public static IppMessage ReadFrom(Stream input, out byte[] trailingData)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            byte[] all = buffer.ToArray();
            IppMessage message = Parse(all, out int length);
            trailingData = length < all.Length ? all.AsSpan(length).ToArray() : Array.Empty<byte>();
            return message;
        }

        private static IppValue ReadValue(ref Reader r, IppTag tag)
        {
            if (tag == IppTag.BeginCollection)
            {
                r.Skip(r.Int16Length()); // begCollection carries an empty value
                return IppValue.Collection(ReadCollection(ref r));
            }

            int length = r.Int16Length();
            switch (tag)
            {
                case IppTag.Integer:
                case IppTag.Enum:
                    Expect(length, 4, tag);
                    return tag == IppTag.Integer ? IppValue.Integer(r.Int32()) : IppValue.Enum(r.Int32());
                case IppTag.Boolean:
                    Expect(length, 1, tag);
                    return IppValue.Boolean(r.Byte() != 0);
                case IppTag.Resolution:
                    Expect(length, 9, tag);
                    return IppValue.Resolution(r.Int32(), r.Int32(), (IppResolutionUnits)r.Byte());
                case IppTag.RangeOfInteger:
                    Expect(length, 8, tag);
                    return IppValue.Range(r.Int32(), r.Int32());
                case IppTag.DateTime:
                    Expect(length, 11, tag);
                    return IppValue.DateTime(r.DateTime());
                case IppTag.TextWithLanguage:
                case IppTag.NameWithLanguage:
                {
                    int end = r.Position + length;
                    string lang = r.String(r.Int16Length(), utf8: false);
                    string text = r.String(r.Int16Length(), utf8: true);
                    if (r.Position != end) throw new IppFormatException("Malformed " + tag + " value.");
                    return tag == IppTag.TextWithLanguage ? IppValue.TextWithLanguage(lang, text) : IppValue.NameWithLanguage(lang, text);
                }
                case IppTag.TextWithoutLanguage: return IppValue.Text(r.String(length, utf8: true));
                case IppTag.NameWithoutLanguage: return IppValue.Name(r.String(length, utf8: true));
                case IppTag.Keyword: return IppValue.Keyword(r.String(length, utf8: false));
                case IppTag.Uri: return IppValue.Uri(r.String(length, utf8: false));
                case IppTag.UriScheme: return IppValue.UriScheme(r.String(length, utf8: false));
                case IppTag.Charset: return IppValue.Charset(r.String(length, utf8: false));
                case IppTag.NaturalLanguage: return IppValue.NaturalLanguage(r.String(length, utf8: false));
                case IppTag.MimeMediaType: return IppValue.MimeMediaType(r.String(length, utf8: false));
                case IppTag.OctetString: return IppValue.OctetString(r.Bytes(length));
                case IppTag.Extension:
                {
                    if (length < 4) throw new IppFormatException("Malformed extension value.");
                    int extensionTag = r.Int32();
                    return IppValue.Raw(IppTag.Extension, r.Bytes(length - 4), extensionTag);
                }
                default:
                    if ((byte)tag >= 0x10 && (byte)tag <= 0x1F)
                    {
                        r.Skip(length);
                        return IppValue.OutOfBand(tag);
                    }
                    return IppValue.Raw(tag, r.Bytes(length));
            }
        }

        private static IppCollection ReadCollection(ref Reader r)
        {
            var collection = new IppCollection();
            IppAttribute? member = null;
            while (true)
            {
                var tag = (IppTag)r.Byte();
                int nameLength = r.Int16Length();
                if (nameLength != 0) throw new IppFormatException("Collection member values must have empty names.");
                if (tag == IppTag.EndCollection)
                {
                    r.Skip(r.Int16Length());
                    return collection;
                }
                if (tag == IppTag.MemberAttrName)
                {
                    member = null;
                    string memberName = r.String(r.Int16Length(), utf8: true);
                    IppValue first = ReadMemberValue(ref r);
                    member = new IppAttribute(memberName, first);
                    collection.Add(member);
                    continue;
                }
                if (member is null) throw new IppFormatException("Collection value without a member name.");
                member.Add(ReadValue(ref r, tag));
            }
        }

        private static IppValue ReadMemberValue(ref Reader r)
        {
            var tag = (IppTag)r.Byte();
            if (r.Int16Length() != 0) throw new IppFormatException("Collection member values must have empty names.");
            if (tag is IppTag.EndCollection or IppTag.MemberAttrName)
                throw new IppFormatException("Collection member without a value.");
            return ReadValue(ref r, tag);
        }

        private static void Expect(int actual, int expected, IppTag tag)
        {
            if (actual != expected) throw new IppFormatException(tag + " value must be " + expected + " bytes, not " + actual + ".");
        }

        // ------------------------------------------------------------------ primitives

        private sealed class Writer
        {
            private readonly Stream _s;
            private readonly byte[] _buf = new byte[11];

            public Writer(Stream s) => _s = s;

            public void Byte(byte b) => _s.WriteByte(b);

            public void Int16(short v)
            {
                BinaryPrimitives.WriteInt16BigEndian(_buf, v);
                _s.Write(_buf, 0, 2);
            }

            public void Int32(int v)
            {
                BinaryPrimitives.WriteInt32BigEndian(_buf, v);
                _s.Write(_buf, 0, 4);
            }

            public void Length(int length)
            {
                if (length > short.MaxValue) throw new InvalidOperationException("IPP value longer than 32767 bytes.");
                Int16((short)length);
            }

            public void Header(IppTag tag, byte[] name)
            {
                Byte((byte)tag);
                Blob(name);
            }

            public void Blob(byte[] bytes)
            {
                Length(bytes.Length);
                _s.Write(bytes, 0, bytes.Length);
            }

            public void Raw(byte[] bytes) => _s.Write(bytes, 0, bytes.Length);

            public void DateTime(DateTimeOffset d)
            {
                TimeSpan offset = d.Offset;
                BinaryPrimitives.WriteUInt16BigEndian(_buf, (ushort)d.Year);
                _buf[2] = (byte)d.Month;
                _buf[3] = (byte)d.Day;
                _buf[4] = (byte)d.Hour;
                _buf[5] = (byte)d.Minute;
                _buf[6] = (byte)d.Second;
                _buf[7] = (byte)(d.Millisecond / 100);
                _buf[8] = offset < TimeSpan.Zero ? (byte)'-' : (byte)'+';
                TimeSpan abs = offset.Duration();
                _buf[9] = (byte)abs.Hours;
                _buf[10] = (byte)abs.Minutes;
                _s.Write(_buf, 0, 11);
            }
        }

        private ref struct Reader
        {
            private readonly ReadOnlySpan<byte> _data;

            public Reader(ReadOnlySpan<byte> data)
            {
                _data = data;
                Position = 0;
            }

            public int Position { get; private set; }

            private ReadOnlySpan<byte> Take(int count)
            {
                if (count < 0 || Position + count > _data.Length)
                    throw new IppFormatException("Truncated IPP message at offset " + Position + ".");
                ReadOnlySpan<byte> slice = _data.Slice(Position, count);
                Position += count;
                return slice;
            }

            public byte Byte() => Take(1)[0];
            public short Int16() => BinaryPrimitives.ReadInt16BigEndian(Take(2));
            public int Int32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

            public int Int16Length()
            {
                int length = BinaryPrimitives.ReadUInt16BigEndian(Take(2));
                if (length > short.MaxValue) throw new IppFormatException("Invalid IPP length " + length + ".");
                return length;
            }

            public void Skip(int count) => Take(count);
            public byte[] Bytes(int count) => Take(count).ToArray();
            public string String(int count, bool utf8) => utf8 ? Encoding.UTF8.GetString(Take(count)) : Encoding.ASCII.GetString(Take(count));

            public DateTimeOffset DateTime()
            {
                ReadOnlySpan<byte> b = Take(11);
                int year = BinaryPrimitives.ReadUInt16BigEndian(b);
                var offset = new TimeSpan(b[9], b[10], 0);
                if (b[8] == '-') offset = -offset;
                try
                {
                    return new DateTimeOffset(year, b[2], b[3], b[4], b[5], b[6], b[7] * 100, offset);
                }
                catch (ArgumentException ex)
                {
                    throw new IppFormatException("Invalid IPP dateTime value.", ex);
                }
            }
        }
    }
}
