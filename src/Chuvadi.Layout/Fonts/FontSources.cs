using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Chuvadi.Layout.Fonts.Internal;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>Cheap identity of a face that a source can supply, without parsing the whole font.</summary>
    public sealed record FontFaceDescriptor(
        string FamilyName,
        string TypographicFamilyName,
        string SubfamilyName,
        string FullName,
        string PostScriptName,
        int Weight,
        bool IsItalic,
        int WidthClass,
        FontOrigin Origin,
        string Key,
        int FaceIndex)
    {
        /// <summary>True when <paramref name="family"/> equals the legacy or typographic family (case-insensitive).</summary>
        public bool MatchesFamily(string family)
            => string.Equals(FamilyName, family, StringComparison.OrdinalIgnoreCase)
               || string.Equals(TypographicFamilyName, family, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A supplier of font faces. Implementations must be thread-safe.</summary>
    public interface IFontSource
    {
        /// <summary>Human-readable name used in reports.</summary>
        string Name { get; }

        /// <summary>The faces this source can supply.</summary>
        IReadOnlyList<FontFaceDescriptor> GetFaces();

        /// <summary>Parses and returns a face this source described.</summary>
        FontFace LoadFace(FontFaceDescriptor descriptor);
    }

    /// <summary>Faces from caller-supplied font bytes. Nothing touches the disk or the network.</summary>
    public sealed class MemoryFontSource : IFontSource
    {
        private readonly object _gate = new object();
        private readonly List<(FontFaceDescriptor Descriptor, FontFace Face)> _faces = new List<(FontFaceDescriptor, FontFace)>();

        public MemoryFontSource(string name = "Supplied fonts", FontOrigin origin = FontOrigin.Supplied)
        {
            Name = name;
            Origin = origin;
        }

        public string Name { get; }

        public FontOrigin Origin { get; }

        /// <summary>Adds every face in <paramref name="fontData"/> (one, or all faces of a collection). Returns the faces added.</summary>
        /// <exception cref="FontFormatException">The data is not a supported sfnt font.</exception>
        public IReadOnlyList<FontFace> Add(ReadOnlyMemory<byte> fontData, string? sourceName = null)
        {
            int count = FontFace.CountFaces(fontData);
            List<FontFace> added = new List<FontFace>(count);
            for (int i = 0; i < count; i++)
            {
                FontFace face = FontFace.Load(fontData, i, Origin, sourceName);
                added.Add(face);
            }
            lock (_gate)
            {
                foreach (FontFace face in added)
                {
                    string key = $"mem:{_faces.Count}";
                    _faces.Add((FontFaceDescriptors.FromFace(face, key), face));
                }
            }
            return added;
        }

        public IReadOnlyList<FontFaceDescriptor> GetFaces()
        {
            lock (_gate)
            {
                List<FontFaceDescriptor> list = new List<FontFaceDescriptor>(_faces.Count);
                foreach ((FontFaceDescriptor d, FontFace _) in _faces)
                {
                    list.Add(d);
                }
                return list;
            }
        }

        public FontFace LoadFace(FontFaceDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            lock (_gate)
            {
                foreach ((FontFaceDescriptor d, FontFace f) in _faces)
                {
                    if (d.Key == descriptor.Key)
                    {
                        return f;
                    }
                }
            }
            throw new ArgumentException($"Face '{descriptor.Key}' does not belong to this source.", nameof(descriptor));
        }
    }

    /// <summary>
    /// Faces read from font files (*.ttf, *.otf, *.ttc, *.otc) in one or more directories. Scanning reads
    /// only the table directory and the name/OS/2/head tables of each file; a face's file is read in full
    /// only when it is loaded. Unreadable or malformed files are skipped. The scan happens once, lazily.
    /// </summary>
    public class DirectoryFontSource : IFontSource
    {
        private static readonly string[] Extensions = { ".ttf", ".otf", ".ttc", ".otc" };

        private readonly object _gate = new object();
        private readonly IReadOnlyList<string> _directories;
        private readonly bool _recursive;
        private readonly FontOrigin _origin;
        private readonly long _maxFileBytes;
        private List<FontFaceDescriptor>? _faces;
        private readonly Dictionary<string, ReadOnlyMemory<byte>> _fileCache = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);

        public DirectoryFontSource(IEnumerable<string> directories, bool recursive = true, FontOrigin origin = FontOrigin.Supplied,
            long maxFileBytes = 128L * 1024 * 1024, string? name = null)
        {
            ArgumentNullException.ThrowIfNull(directories);
            _directories = new List<string>(directories);
            _recursive = recursive;
            _origin = origin;
            _maxFileBytes = maxFileBytes;
            Name = name ?? "Font directories: " + string.Join(";", _directories);
        }

        public DirectoryFontSource(string directory, bool recursive = true)
            : this(new[] { directory }, recursive)
        {
        }

        public string Name { get; }

        public IReadOnlyList<FontFaceDescriptor> GetFaces()
        {
            lock (_gate)
            {
                _faces ??= Scan();
                return _faces;
            }
        }

        public FontFace LoadFace(FontFaceDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ReadOnlyMemory<byte> bytes;
            lock (_gate)
            {
                if (!_fileCache.TryGetValue(descriptor.Key, out bytes))
                {
                    bytes = File.ReadAllBytes(descriptor.Key);
                    _fileCache[descriptor.Key] = bytes;
                }
            }
            return FontFace.Load(bytes, descriptor.FaceIndex, _origin, Path.GetFileName(descriptor.Key));
        }

        private List<FontFaceDescriptor> Scan()
        {
            List<FontFaceDescriptor> result = new List<FontFaceDescriptor>();
            SearchOption option = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string dir in _directories)
            {
                IEnumerable<string> files;
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }
                    files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = option == SearchOption.AllDirectories,
                        IgnoreInaccessible = true,
                        MatchCasing = MatchCasing.CaseInsensitive,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file);
                    if (Array.FindIndex(Extensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) < 0)
                    {
                        continue;
                    }
                    try
                    {
                        using FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.RandomAccess);
                        if (fs.Length > _maxFileBytes || fs.Length < 12)
                        {
                            continue;
                        }
                        result.AddRange(FontFaceDescriptors.Describe(fs, file, _origin));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FontFormatException)
                    {
                        // Skip unreadable or malformed files.
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// The operating system's font directories: Windows (%WINDIR%\Fonts and the per-user
    /// %LOCALAPPDATA%\Microsoft\Windows\Fonts), macOS (/System/Library/Fonts, /Library/Fonts, ~/Library/Fonts)
    /// and Linux (/usr/share/fonts, /usr/local/share/fonts, ~/.local/share/fonts, ~/.fonts).
    /// Used only when <see cref="LayoutOptions.AllowSystemFonts"/> is true; results then depend on the machine.
    /// </summary>
    public sealed class SystemFontSource : DirectoryFontSource
    {
        public SystemFontSource()
            : base(DefaultDirectories(), recursive: true, origin: FontOrigin.System, name: "System fonts")
        {
        }

        /// <summary>The directories scanned on the current OS.</summary>
        public static IReadOnlyList<string> DefaultDirectories()
        {
            List<string> dirs = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (windir.Length > 0)
                {
                    dirs.Add(Path.Combine(windir, "Fonts"));
                }
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (local.Length > 0)
                {
                    dirs.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                dirs.Add("/System/Library/Fonts");
                dirs.Add("/Library/Fonts");
                if (home.Length > 0)
                {
                    dirs.Add(Path.Combine(home, "Library", "Fonts"));
                }
            }
            else
            {
                dirs.Add("/usr/share/fonts");
                dirs.Add("/usr/local/share/fonts");
                if (home.Length > 0)
                {
                    dirs.Add(Path.Combine(home, ".local", "share", "fonts"));
                    dirs.Add(Path.Combine(home, ".fonts"));
                }
            }
            return dirs;
        }
    }

    /// <summary>Builds descriptors cheaply from a seekable stream or from a parsed face.</summary>
    internal static class FontFaceDescriptors
    {
        public static FontFaceDescriptor FromFace(FontFace f, string key) => new FontFaceDescriptor(
            f.FamilyName, f.TypographicFamilyName, f.SubfamilyName, f.FullName, f.PostScriptName,
            f.Weight, f.IsItalic, f.WidthClass, f.Origin, key, f.FaceIndex);

        /// <summary>Reads only the directories and the head/name/OS/2 tables of each face in the stream.</summary>
        public static List<FontFaceDescriptor> Describe(Stream stream, string key, FontOrigin origin)
        {
            byte[] header = ReadAt(stream, 0, (int)Math.Min(stream.Length, 12));
            BigEndian h = new BigEndian(header);
            List<long> dirOffsets = new List<long>();
            if (h.U32(0) == 0x74746366)
            {
                uint n = h.U32(8);
                if (n == 0 || n > 4096)
                {
                    throw new FontFormatException("Bad collection header.");
                }
                BigEndian offs = new BigEndian(ReadAt(stream, 12, checked((int)(4 * n))));
                for (int i = 0; i < n; i++)
                {
                    dirOffsets.Add(offs.U32(4L * i));
                }
            }
            else
            {
                dirOffsets.Add(0);
            }

            List<FontFaceDescriptor> result = new List<FontFaceDescriptor>();
            for (int faceIndex = 0; faceIndex < dirOffsets.Count; faceIndex++)
            {
                long dirOffset = dirOffsets[faceIndex];
                BigEndian d = new BigEndian(ReadAt(stream, dirOffset, 12));
                uint sig = d.U32(0);
                if (sig != 0x00010000 && sig != 0x74727565 && sig != 0x4F54544F)
                {
                    continue;
                }
                int numTables = d.U16(4);
                if (numTables == 0 || numTables > 512)
                {
                    continue;
                }
                BigEndian recs = new BigEndian(ReadAt(stream, dirOffset + 12, 16 * numTables));
                (long Off, int Len) name = default, os2 = default, head = default;
                bool hasCmap = false;
                for (int i = 0; i < numTables; i++)
                {
                    string tag = recs.Tag(16L * i);
                    long off = recs.U32((16L * i) + 8);
                    int len = (int)Math.Min(recs.U32((16L * i) + 12), 1 << 20);
                    switch (tag)
                    {
                        case "name": name = (off, len); break;
                        case "OS/2": os2 = (off, len); break;
                        case "head": head = (off, len); break;
                        case "cmap": hasCmap = true; break;
                    }
                }
                if (!hasCmap || head.Len < 54)
                {
                    continue;
                }
                BigEndian headT = new BigEndian(ReadAt(stream, head.Off, 54));
                int macStyle = headT.U16(44);
                Dictionary<int, string> names = name.Len > 0
                    ? NameTable.Read(new BigEndian(ReadAt(stream, name.Off, name.Len)))
                    : new Dictionary<int, string>();
                int weight = (macStyle & 1) != 0 ? 700 : 400;
                bool italic = (macStyle & 2) != 0;
                int width = 5;
                if (os2.Len >= 64)
                {
                    BigEndian o = new BigEndian(ReadAt(stream, os2.Off, 64));
                    weight = Math.Clamp((int)o.U16(4), 1, 1000);
                    width = Math.Clamp((int)o.U16(6), 1, 9);
                    int fsSelection = o.U16(62);
                    italic |= (fsSelection & 1) != 0;
                    if ((fsSelection & 0x20) != 0 && weight < 600)
                    {
                        weight = 700;
                    }
                }
                string family = names.GetValueOrDefault(1) ?? names.GetValueOrDefault(16) ?? string.Empty;
                if (family.Length == 0)
                {
                    continue;
                }
                string typo = names.GetValueOrDefault(16) ?? family;
                string sub = names.GetValueOrDefault(2) ?? string.Empty;
                string full = names.GetValueOrDefault(4) ?? (family + " " + sub).Trim();
                string ps = names.GetValueOrDefault(6) ?? full.Replace(" ", string.Empty, StringComparison.Ordinal);
                result.Add(new FontFaceDescriptor(family, typo, sub, full, ps, weight, italic, width, origin, key, faceIndex));
            }
            return result;
        }

        private static byte[] ReadAt(Stream s, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > s.Length)
            {
                throw new FontFormatException("Font table lies outside the file.");
            }
            byte[] buffer = new byte[count];
            s.Position = offset;
            s.ReadExactly(buffer, 0, count);
            return buffer;
        }
    }
}
