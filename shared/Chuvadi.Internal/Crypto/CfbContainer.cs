using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// Compound File Binary (CFB / MS-CFB) writer and reader, structured to match what Excel
/// produces for encrypted OOXML files.
///
/// Supports:
/// - Nested storages (parent/child via Child + Left/Right siblings).
/// - MiniFAT for small streams (under MiniStreamCutoff bytes, default 4096).
/// - Regular FAT for large streams.
/// - Multiple FAT sectors (DIFAT inline up to 109 entries → ~7 MB of content with 512-byte
///   sectors; the reader additionally follows chained DIFAT sectors for larger files).
/// - Red-black-ish tree directory layout (we use balanced left/right pointers).
///
/// Sector model: Version 3 (512-byte sectors, 64-byte mini sectors). This matches Excel's
/// output exactly and is the most broadly compatible variant.
/// </summary>
internal static class CfbContainer
{
    // ---- Constants ---------------------------------------------------------------

    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int DirectoryEntrySize = 128;
    private const int MiniStreamCutoff = 4096;

    private const uint Free = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSectorMarker = 0xFFFFFFFC;
    private const uint NoStream = 0xFFFFFFFF;

    private static readonly byte[] HeaderSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    // Directory entry types
    public const byte TypeUnallocated = 0;
    public const byte TypeStorage = 1;
    public const byte TypeStream = 2;
    public const byte TypeRoot = 5;

    // Colors for red-black tree (0 = red, 1 = black)
    public const byte ColorRed = 0;
    public const byte ColorBlack = 1;

    // ---- Public model -------------------------------------------------------------

    /// <summary>
    /// One node in the CFB hierarchy. Either a stream (with Data) or a storage (with Children).
    /// </summary>
    public sealed class Node
    {
        public required string Name { get; init; }
        public required byte Type { get; init; }  // TypeStorage or TypeStream (root is added by writer)
        public byte[]? Data { get; init; }  // For streams
        public List<Node> Children { get; init; } = new();
        public byte Color { get; set; } = ColorRed;  // Red by default (matches Excel)
    }

    // ---- Internal state used during write ---------------------------------------

    private sealed class WriteContext
    {
        public List<Node> AllEntries = new();  // Flattened, in directory order
        public Dictionary<Node, int> EntryIndex = new();
        public List<byte> RegularStreamContent = new();  // Concatenated content for non-mini streams
        public Dictionary<Node, uint> RegularStreamStartSector = new();
        public List<byte> MiniStreamContent = new();  // Concatenated content for mini streams
        public Dictionary<Node, uint> MiniStreamStartSector = new();
    }

    // ---- Writer -----------------------------------------------------------------

    /// <summary>
    /// Writes a CFB file with the given root-level nodes. The function constructs the directory
    /// tree, allocates sectors for streams (regular FAT or MiniFAT based on size), builds the
    /// FATs, and writes the file in one pass.
    /// </summary>
    public static void Write(Stream output, IReadOnlyList<Node> rootChildren)
    {
        // 1. Build flat directory entry list and assign indices.
        var ctx = new WriteContext();

        // Root entry — always index 0.
        var rootNode = new Node
        {
            Name = "Root Entry",
            Type = TypeRoot,
            Children = new List<Node>(rootChildren),
            Color = ColorRed,  // Excel uses 0 (red) for root
        };
        ctx.AllEntries.Add(rootNode);
        ctx.EntryIndex[rootNode] = 0;

        // Traverse and assign indices to all descendants (BFS yields the order Excel uses).
        AssignIndices(rootNode, ctx);

        // 2. Allocate sector positions for stream content.
        AllocateStreamSectors(ctx);

        // 3. Build directory entries with proper sibling/child pointers.
        var dirBytes = BuildDirectoryEntries(ctx);

        // 4. Compute layout:
        //    Sector 0 = file header (always)
        //    Then content sectors (regular streams' bytes including mini-stream container)
        //    Then mini-FAT sectors (chain describing mini-stream-container layout)
        //    Then directory sectors
        //    Then FAT sectors
        //
        // Sector indices below are 0-based (sector 0 = first sector AFTER the 512-byte header).

        int currentSector = 0;

        // Layout regular stream content (now also includes the mini-stream container appended).
        int regularContentSectors = (ctx.RegularStreamContent.Count + SectorSize - 1) / SectorSize;
        int regularContentStart = currentSector;
        currentSector += regularContentSectors;

        // Mini-FAT sectors describe the mini-stream-container layout.
        // Each MiniFAT sector holds 128 entries. We need one entry per mini sector used.
        int miniSectorCount = (ctx.MiniStreamContent.Count + MiniSectorSize - 1) / MiniSectorSize;
        int miniFatSectors = miniSectorCount > 0 ? (miniSectorCount + 127) / 128 : 0;
        int miniFatStart = currentSector;
        currentSector += miniFatSectors;

        // Directory sectors: 4 entries per sector.
        int directorySectors = (ctx.AllEntries.Count + 3) / 4;
        int directoryStart = currentSector;
        currentSector += directorySectors;

        // FAT sectors: 128 entries each. We need one entry per sector in the entire file.
        int fatSectors = 0;
        int totalSectors;
        while (true)
        {
            int needed = (currentSector + fatSectors + 127) / 128;
            if (needed == fatSectors) break;
            fatSectors = needed;
        }
        int fatStart = currentSector;
        totalSectors = currentSector + fatSectors;

        // 5. Set root entry's startSector to the mini-stream container.
        //    The mini-stream container is appended at the end of the regular stream content.
        //    Excel: Root.startSector = the sector where the mini-stream container begins.
        uint miniContainerStartSector = 0xFFFFFFFE;
        if (ctx.MiniStreamContent.Count > 0)
        {
            // The mini-stream container's sector index in the regular FAT.
            // We tacked it onto the end of RegularStreamContent below.
            // Recompute: figure out where in RegularStreamContent the mini container starts.
            int miniContainerByteOffset = ctx.RegularStreamContent.Count;
            // Append mini-stream content padded to sector boundary.
            int padded = (ctx.MiniStreamContent.Count + SectorSize - 1) / SectorSize * SectorSize;
            ctx.RegularStreamContent.AddRange(ctx.MiniStreamContent);
            while (ctx.RegularStreamContent.Count < miniContainerByteOffset + padded)
                ctx.RegularStreamContent.Add(0);

            miniContainerStartSector = (uint)(regularContentStart + miniContainerByteOffset / SectorSize);

            // Adjust total sector counts since we just added content.
            int newRegularSectors = (ctx.RegularStreamContent.Count + SectorSize - 1) / SectorSize;
            int delta = newRegularSectors - regularContentSectors;
            regularContentSectors = newRegularSectors;

            // Shift everything after.
            miniFatStart += delta;
            directoryStart += delta;
            fatStart += delta;
            currentSector += delta;
            // Recompute FAT sector count with the new total.
            fatSectors = 0;
            while (true)
            {
                int needed = (currentSector + fatSectors + 127) / 128;
                if (needed == fatSectors) break;
                fatSectors = needed;
            }
            fatStart = currentSector;
            totalSectors = currentSector + fatSectors;
        }

        // Update root entry's startSector and size now that we know them.
        SetRootStartSectorAndSize(dirBytes, miniContainerStartSector, (ulong)ctx.MiniStreamContent.Count);

        // 6. Build the FAT.
        var fat = new uint[fatSectors * 128];
        for (int i = 0; i < fat.Length; i++) fat[i] = Free;

        // Chain regular stream sectors.
        // Each stream occupies a contiguous run of sectors; chain them as a linked list.
        foreach (var kv in ctx.RegularStreamStartSector)
        {
            int sectorsForThis = (kv.Key.Data!.Length + SectorSize - 1) / SectorSize;
            uint start = kv.Value;
            for (int j = 0; j < sectorsForThis; j++)
            {
                bool last = (j == sectorsForThis - 1);
                fat[start + j] = last ? EndOfChain : (uint)(start + j + 1);
            }
        }

        // Chain the mini-stream container if present.
        if (ctx.MiniStreamContent.Count > 0)
        {
            int miniContainerSectors = (ctx.MiniStreamContent.Count + SectorSize - 1) / SectorSize;
            for (int j = 0; j < miniContainerSectors; j++)
            {
                bool last = (j == miniContainerSectors - 1);
                fat[miniContainerStartSector + j] = last ? EndOfChain : (uint)(miniContainerStartSector + j + 1);
            }
        }

        // Chain the MiniFAT sectors.
        for (int j = 0; j < miniFatSectors; j++)
        {
            bool last = (j == miniFatSectors - 1);
            fat[miniFatStart + j] = last ? EndOfChain : (uint)(miniFatStart + j + 1);
        }

        // Chain the directory sectors.
        for (int j = 0; j < directorySectors; j++)
        {
            bool last = (j == directorySectors - 1);
            fat[directoryStart + j] = last ? EndOfChain : (uint)(directoryStart + j + 1);
        }

        // Mark FAT sectors themselves.
        for (int j = 0; j < fatSectors; j++) fat[fatStart + j] = FatSector;

        // 7. Build the MiniFAT.
        var miniFat = new uint[miniFatSectors * 128];
        for (int i = 0; i < miniFat.Length; i++) miniFat[i] = Free;
        foreach (var kv in ctx.MiniStreamStartSector)
        {
            int sectorsForThis = (kv.Key.Data!.Length + MiniSectorSize - 1) / MiniSectorSize;
            uint start = kv.Value;
            for (int j = 0; j < sectorsForThis; j++)
            {
                bool last = (j == sectorsForThis - 1);
                miniFat[start + j] = last ? EndOfChain : (uint)(start + j + 1);
            }
        }

        // 8. Write the file.
        output.Position = 0;
        WriteHeader(output, fatSectors, fatStart, directorySectors, directoryStart,
            miniFatSectors, miniFatStart, miniContainerStartSector);

        // Pad header to sector boundary.
        if (output.Position < SectorSize)
        {
            var pad = new byte[SectorSize - (int)output.Position];
            output.Write(pad, 0, pad.Length);
        }

        // Write content sectors (regular streams + mini-stream container, padded).
        var regularBytes = ctx.RegularStreamContent.ToArray();
        output.Write(regularBytes, 0, regularBytes.Length);
        // Pad to next sector boundary.
        int regularPad = regularContentSectors * SectorSize - regularBytes.Length;
        if (regularPad > 0) output.Write(new byte[regularPad], 0, regularPad);

        // Write MiniFAT sectors.
        if (miniFatSectors > 0)
        {
            var miniFatBytes = new byte[miniFatSectors * SectorSize];
            for (int i = 0; i < miniFat.Length; i++)
                WriteUInt32(miniFatBytes, i * 4, miniFat[i]);
            output.Write(miniFatBytes, 0, miniFatBytes.Length);
        }

        // Write directory sectors.
        output.Write(dirBytes, 0, dirBytes.Length);
        // Pad to sector boundary if needed.
        int dirPad = directorySectors * SectorSize - dirBytes.Length;
        if (dirPad > 0) output.Write(new byte[dirPad], 0, dirPad);

        // Write FAT sectors.
        var fatBytes = new byte[fatSectors * SectorSize];
        for (int i = 0; i < fat.Length; i++)
            WriteUInt32(fatBytes, i * 4, fat[i]);
        output.Write(fatBytes, 0, fatBytes.Length);

        output.Flush();
    }

    /// <summary>
    /// Assigns directory indices via pre-order DFS, matching Excel's actual ordering.
    /// Excel visits each node, then recursively visits all its descendants, before moving
    /// to the next sibling.
    /// </summary>
    private static void AssignIndices(Node root, WriteContext ctx)
    {
        // Pre-order DFS: visit root's children in declaration order, expanding each child's
        // subtree before moving to the next sibling. This matches the reference file:
        //   [1] EncryptedPackage, [2] \x06DataSpaces, [3] Version, [4] DataSpaceMap,
        //   [5] DataSpaceInfo, [6] StrongEncryptionDataSpace, [7] TransformInfo,
        //   [8] StrongEncryptionTransform, [9] \x06Primary, [10] EncryptionInfo.

        foreach (var child in root.Children) DfsAssign(child, ctx);
    }

    private static void DfsAssign(Node node, WriteContext ctx)
    {
        int idx = ctx.AllEntries.Count;
        ctx.AllEntries.Add(node);
        ctx.EntryIndex[node] = idx;
        foreach (var child in node.Children) DfsAssign(child, ctx);
    }

    /// <summary>
    /// Decides for each stream whether it goes in the regular FAT or MiniFAT, and computes
    /// its start sector. Streams under MiniStreamCutoff bytes go in the MiniFAT.
    /// </summary>
    private static void AllocateStreamSectors(WriteContext ctx)
    {
        foreach (var node in ctx.AllEntries)
        {
            if (node.Type != TypeStream || node.Data == null || node.Data.Length == 0)
                continue;

            if (node.Data.Length < MiniStreamCutoff)
            {
                // MiniFAT
                int byteOffset = ctx.MiniStreamContent.Count;
                ctx.MiniStreamContent.AddRange(node.Data);
                // Pad to mini sector boundary
                while (ctx.MiniStreamContent.Count % MiniSectorSize != 0)
                    ctx.MiniStreamContent.Add(0);
                ctx.MiniStreamStartSector[node] = (uint)(byteOffset / MiniSectorSize);
            }
            else
            {
                // Regular FAT
                int byteOffset = ctx.RegularStreamContent.Count;
                ctx.RegularStreamContent.AddRange(node.Data);
                // Pad to sector boundary
                while (ctx.RegularStreamContent.Count % SectorSize != 0)
                    ctx.RegularStreamContent.Add(0);
                ctx.RegularStreamStartSector[node] = (uint)(byteOffset / SectorSize);
            }
        }
    }

    /// <summary>
    /// Builds directory entries with proper sibling pointers (matching Excel's red-black tree).
    /// </summary>
    private static byte[] BuildDirectoryEntries(WriteContext ctx)
    {
        int directorySectors = (ctx.AllEntries.Count + 3) / 4;
        var buf = new byte[directorySectors * SectorSize];

        // Fill any unused entries with all 0xFF for type fields beyond entry count.
        // Actually CFB convention: unallocated entries have type=0 and clear left/right/child = -1.
        for (int i = ctx.AllEntries.Count; i < directorySectors * 4; i++)
        {
            int off = i * DirectoryEntrySize;
            WriteUInt32(buf, off + 68, NoStream);
            WriteUInt32(buf, off + 72, NoStream);
            WriteUInt32(buf, off + 76, NoStream);
            WriteUInt32(buf, off + 116, EndOfChain);
        }

        // For each storage (including root), compute the sibling tree for its children.
        // Excel uses a balanced binary tree where the parent's "child" pointer goes to the
        // median entry, and left/right siblings form the tree. For 1 child: child→entry, L/R=-1.
        // For multiple children: pick the median as the root, recurse.

        // Build a map: storage node → list of child node indices.
        var storageToChildIndices = new Dictionary<Node, List<int>>();
        foreach (var node in ctx.AllEntries)
        {
            if (node.Type == TypeStorage || node.Type == TypeRoot)
                storageToChildIndices[node] = node.Children.Select(c => ctx.EntryIndex[c]).ToList();
        }

        // For each storage, set its child pointer to the "median" of its children (CFB's red-black
        // convention) and recursively assign left/right siblings.
        var siblingLeft = new Dictionary<int, int>();
        var siblingRight = new Dictionary<int, int>();
        var storageChildPointer = new Dictionary<int, int>();

        foreach (var kv in storageToChildIndices)
        {
            var parentIdx = ctx.EntryIndex[kv.Key];
            var children = kv.Value;
            if (children.Count == 0)
            {
                storageChildPointer[parentIdx] = -1;
                continue;
            }

            // CFB sort order: by name length, then by uppercase ordinal codepoints.
            // Excel uses this for sibling tree construction.
            var sorted = children.OrderBy(i => ctx.AllEntries[i].Name.Length)
                                 .ThenBy(i => ctx.AllEntries[i].Name.ToUpperInvariant(), StringComparer.Ordinal)
                                 .ToList();

            BuildBalancedTree(sorted, 0, sorted.Count - 1, siblingLeft, siblingRight);
            // The parent's child pointer goes to the median (root of the tree).
            storageChildPointer[parentIdx] = sorted[(sorted.Count - 1) / 2];
        }

        // Write each entry.
        for (int i = 0; i < ctx.AllEntries.Count; i++)
        {
            var node = ctx.AllEntries[i];
            int off = i * DirectoryEntrySize;

            // Name (UTF-16 LE, including the terminator)
            var nameBytes = Encoding.Unicode.GetBytes(node.Name);
            if (nameBytes.Length > 62) throw new ArgumentException($"Stream name too long: {node.Name}");
            Array.Copy(nameBytes, 0, buf, off, nameBytes.Length);
            WriteUInt16(buf, off + 64, (ushort)(nameBytes.Length + 2));

            // Type & color
            buf[off + 66] = node.Type;
            buf[off + 67] = node.Color;

            // Sibling pointers
            int left = siblingLeft.TryGetValue(i, out var l) ? l : -1;
            int right = siblingRight.TryGetValue(i, out var r) ? r : -1;
            WriteUInt32(buf, off + 68, left < 0 ? NoStream : (uint)left);
            WriteUInt32(buf, off + 72, right < 0 ? NoStream : (uint)right);

            // Child pointer (for storages and root)
            if (node.Type == TypeStorage || node.Type == TypeRoot)
            {
                int childIdx = storageChildPointer.TryGetValue(i, out var c) ? c : -1;
                WriteUInt32(buf, off + 76, childIdx < 0 ? NoStream : (uint)childIdx);
            }
            else
            {
                WriteUInt32(buf, off + 76, NoStream);
            }

            // CLSID (16 bytes at offset 80) = all zeros.
            // State bits, creation/modify times (offsets 96-115) = zero.

            // Start sector & size
            if (node.Type == TypeStream && node.Data != null && node.Data.Length > 0)
            {
                if (node.Data.Length < MiniStreamCutoff)
                    WriteUInt32(buf, off + 116, ctx.MiniStreamStartSector[node]);
                else
                    WriteUInt32(buf, off + 116, ctx.RegularStreamStartSector[node]);

                WriteUInt64(buf, off + 120, (ulong)node.Data.Length);
            }
            else if (node.Type == TypeRoot)
            {
                // Root start sector + size set later by SetRootStartSectorAndSize().
                WriteUInt32(buf, off + 116, EndOfChain);
                WriteUInt64(buf, off + 120, 0);
            }
            else
            {
                // Storages: start sector field = 0, size = 0 (matches Excel).
                WriteUInt32(buf, off + 116, 0);
                WriteUInt64(buf, off + 120, 0);
            }
        }

        return buf;
    }

    /// <summary>
    /// Builds a balanced binary tree over the sorted index list, populating left/right sibling
    /// pointer maps. The "root" of the tree is at index (count-1)/2 of the sorted list.
    /// </summary>
    private static void BuildBalancedTree(List<int> sorted, int lo, int hi,
        Dictionary<int, int> left, Dictionary<int, int> right)
    {
        if (lo > hi) return;
        int mid = (lo + hi) / 2;
        int node = sorted[mid];

        if (lo <= mid - 1)
        {
            int leftMid = (lo + mid - 1) / 2;
            left[node] = sorted[leftMid];
            BuildBalancedTree(sorted, lo, mid - 1, left, right);
        }
        else
        {
            left[node] = -1;
        }

        if (mid + 1 <= hi)
        {
            int rightMid = (mid + 1 + hi) / 2;
            right[node] = sorted[rightMid];
            BuildBalancedTree(sorted, mid + 1, hi, left, right);
        }
        else
        {
            right[node] = -1;
        }
    }

    private static void SetRootStartSectorAndSize(byte[] dirBytes, uint startSector, ulong size)
    {
        WriteUInt32(dirBytes, 0 * DirectoryEntrySize + 116, startSector);
        WriteUInt64(dirBytes, 0 * DirectoryEntrySize + 120, size);
    }

    private static void WriteHeader(Stream output, int numFatSectors, int firstFatSector,
        int numDirSectors, int firstDirSector, int numMiniFatSectors, int firstMiniFatSector,
        uint firstMiniContainerSector)
    {
        // Reuse internal helpers to write header fields in one pass.
        var hdr = new byte[SectorSize];
        Array.Copy(HeaderSignature, 0, hdr, 0, 8);
        // CLSID (16 bytes at offset 8) = zero.
        WriteUInt16(hdr, 24, 0x003E);  // Minor version (matches Excel)
        WriteUInt16(hdr, 26, 3);       // Major version (Version 3 = 512-byte sectors)
        WriteUInt16(hdr, 28, 0xFFFE);  // Little-endian
        WriteUInt16(hdr, 30, 9);       // Sector shift (1 << 9 = 512)
        WriteUInt16(hdr, 32, 6);       // Mini sector shift (1 << 6 = 64)
        // Reserved bytes at 34..40 = zero.
        WriteUInt32(hdr, 40, 0);  // Number of directory sectors (must be 0 for v3)
        WriteUInt32(hdr, 44, (uint)numFatSectors);
        WriteUInt32(hdr, 48, (uint)firstDirSector);
        WriteUInt32(hdr, 52, 0);  // Transaction signature
        WriteUInt32(hdr, 56, MiniStreamCutoff);
        WriteUInt32(hdr, 60, numMiniFatSectors > 0 ? (uint)firstMiniFatSector : EndOfChain);
        WriteUInt32(hdr, 64, (uint)numMiniFatSectors);
        WriteUInt32(hdr, 68, EndOfChain);  // First DIFAT sector — none (we fit in header DIFAT)
        WriteUInt32(hdr, 72, 0);  // DIFAT sector count

        // DIFAT (109 entries starting at offset 76)
        for (int i = 0; i < 109; i++) WriteUInt32(hdr, 76 + i * 4, Free);
        for (int i = 0; i < numFatSectors && i < 109; i++)
            WriteUInt32(hdr, 76 + i * 4, (uint)(firstFatSector + i));

        output.Write(hdr, 0, hdr.Length);
    }

    // ---- Reader -----------------------------------------------------------------

    /// <summary>
    /// Reads a CFB file and returns a flat dictionary of stream name → bytes.
    /// </summary>
    public static Dictionary<string, byte[]> Read(Stream input)
    {
        input.Position = 0;
        var headerBytes = new byte[SectorSize];
        input.ReadExactly(headerBytes, 0, SectorSize);

        for (int i = 0; i < HeaderSignature.Length; i++)
            if (headerBytes[i] != HeaderSignature[i])
                throw new InvalidDataException("Not a CFB file (signature mismatch).");

        uint firstDirSector = ReadUInt32(headerBytes, 48);
        uint firstMiniFatSector = ReadUInt32(headerBytes, 60);
        uint miniStreamCutoff = ReadUInt32(headerBytes, 56);
        uint firstDifatSector = ReadUInt32(headerBytes, 68);
        uint difatSectorCount = ReadUInt32(headerBytes, 72);

        // DIFAT — first 109 entries are in the header; larger files chain additional DIFAT
        // sectors (127 FAT-sector pointers each, plus a trailing next-sector pointer).
        var difat = new List<uint>();
        for (int i = 0; i < 109; i++)
        {
            var entry = ReadUInt32(headerBytes, 76 + i * 4);
            if (entry >= 0xFFFFFFF0) continue;  // Free / markers — skip, keep scanning.
            difat.Add(entry);
        }
        {
            uint cur = firstDifatSector;
            uint safety = difatSectorCount + 16;
            var difatBuf = new byte[SectorSize];
            while (cur < 0xFFFFFFF0 && safety-- > 0)
            {
                input.Position = (long)SectorSize + (long)cur * SectorSize;
                input.ReadExactly(difatBuf, 0, SectorSize);
                for (int i = 0; i < SectorSize - 4; i += 4)
                {
                    var entry = ReadUInt32(difatBuf, i);
                    if (entry < 0xFFFFFFF0) difat.Add(entry);
                }
                cur = ReadUInt32(difatBuf, SectorSize - 4);  // Next DIFAT sector in the chain.
            }
        }

        // Read FAT.
        var fat = new List<uint>();
        foreach (var fatSectorIdx in difat)
        {
            input.Position = (long)SectorSize + (long)fatSectorIdx * SectorSize;
            var fatBuf = new byte[SectorSize];
            input.ReadExactly(fatBuf, 0, SectorSize);
            for (int i = 0; i < SectorSize; i += 4) fat.Add(ReadUInt32(fatBuf, i));
        }

        // Read directory chain.
        var dirData = ReadFatChain(input, fat, firstDirSector);

        // Read MiniFAT (if present).
        var miniFat = new List<uint>();
        if (firstMiniFatSector < 0xFFFFFFF0)
        {
            var miniFatBytes = ReadFatChain(input, fat, firstMiniFatSector);
            for (int i = 0; i + 4 <= miniFatBytes.Length; i += 4) miniFat.Add(ReadUInt32(miniFatBytes, i));
        }

        // Find root's start sector (the mini-stream container).
        uint rootContainerStart = ReadUInt32(dirData, 0 * DirectoryEntrySize + 116);
        byte[] miniContainer = Array.Empty<byte>();
        if (rootContainerStart < 0xFFFFFFF0 && miniFat.Count > 0)
            miniContainer = ReadFatChain(input, fat, rootContainerStart);

        // Parse directory entries.
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int entryCount = dirData.Length / DirectoryEntrySize;
        for (int i = 0; i < entryCount; i++)
        {
            int off = i * DirectoryEntrySize;
            byte type = dirData[off + 66];
            if (type != TypeStream) continue;

            int nameLen = ReadUInt16(dirData, off + 64);
            // The name field is 64 bytes; nameLen includes the UTF-16 null terminator.
            // Clamp against malformed/hostile files that declare an out-of-range length.
            if (nameLen <= 2 || nameLen > 64) continue;
            var name = Encoding.Unicode.GetString(dirData, off, nameLen - 2);

            uint startSec = ReadUInt32(dirData, off + 116);
            ulong size = ReadUInt64(dirData, off + 120);

            if (size == 0 || startSec >= 0xFFFFFFF0)
            {
                result[name] = Array.Empty<byte>();
                continue;
            }

            byte[] content;
            if (size < miniStreamCutoff && miniFat.Count > 0)
            {
                content = ReadMiniChain(miniContainer, miniFat, startSec, (int)size);
            }
            else
            {
                var chain = ReadFatChain(input, fat, startSec);
                if (chain.Length > (int)size)
                {
                    content = new byte[(int)size];
                    Array.Copy(chain, 0, content, 0, (int)size);
                }
                else
                {
                    content = chain;
                }
            }
            result[name] = content;
        }

        return result;
    }

    private static byte[] ReadFatChain(Stream input, List<uint> fat, uint startSector)
    {
        var collected = new List<byte>(SectorSize * 4);
        uint cur = startSector;
        int safety = fat.Count + 16;
        while (cur < 0xFFFFFFF0 && safety-- > 0)
        {
            input.Position = (long)SectorSize + (long)cur * SectorSize;
            var buf = new byte[SectorSize];
            int r = input.ReadAtLeast(buf, SectorSize, throwOnEndOfStream: false);
            if (r <= 0) break;
            for (int i = 0; i < r; i++) collected.Add(buf[i]);
            if (cur >= fat.Count) break;
            cur = fat[(int)cur];
        }
        return collected.ToArray();
    }

    private static byte[] ReadMiniChain(byte[] miniContainer, List<uint> miniFat, uint startSector, int size)
    {
        var output = new byte[size];
        int outPos = 0;
        uint cur = startSector;
        int safety = miniFat.Count + 16;
        while (cur < 0xFFFFFFF0 && safety-- > 0 && outPos < size)
        {
            int srcOff = (int)cur * MiniSectorSize;
            if (srcOff >= miniContainer.Length) break;
            int chunk = Math.Min(MiniSectorSize, size - outPos);
            if (srcOff + chunk > miniContainer.Length) chunk = miniContainer.Length - srcOff;
            if (chunk <= 0) break;
            Array.Copy(miniContainer, srcOff, output, outPos, chunk);
            outPos += chunk;
            if (cur >= miniFat.Count) break;
            cur = miniFat[(int)cur];
        }
        return output;
    }

    // ---- Byte helpers ------------------------------------------------------------

    private static void WriteUInt16(byte[] buf, int off, ushort v)
    {
        buf[off] = (byte)v;
        buf[off + 1] = (byte)(v >> 8);
    }

    private static void WriteUInt32(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)v;
        buf[off + 1] = (byte)(v >> 8);
        buf[off + 2] = (byte)(v >> 16);
        buf[off + 3] = (byte)(v >> 24);
    }

    private static void WriteUInt64(byte[] buf, int off, ulong v)
    {
        WriteUInt32(buf, off, (uint)v);
        WriteUInt32(buf, off + 4, (uint)(v >> 32));
    }

    private static ushort ReadUInt16(byte[] buf, int off) => (ushort)(buf[off] | (buf[off + 1] << 8));

    private static uint ReadUInt32(byte[] buf, int off) =>
        (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));

    private static ulong ReadUInt64(byte[] buf, int off) =>
        ReadUInt32(buf, off) | ((ulong)ReadUInt32(buf, off + 4) << 32);
}
