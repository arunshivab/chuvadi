using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// Takes a plaintext OOXML package (the unencrypted xlsx zip bytes), encrypts it via agile
/// encryption, and writes the result as a CFB container with the full Excel-compatible
/// structure: EncryptedPackage at root, \x06DataSpaces sub-tree, EncryptionInfo at root.
/// </summary>
internal static class EncryptedPackageWriter
{
    public static void WriteEncrypted(Stream output, byte[] plaintextPackage, string password, int spinCount = 100_000)
    {
        using var ms = new MemoryStream(plaintextPackage, writable: false);
        WriteEncrypted(output, ms, plaintextPackage.Length, password, spinCount);
    }

    /// <summary>
    /// Stream-based variant: reads the plaintext package from <paramref name="plaintextPackage"/>
    /// (exactly <paramref name="length"/> bytes from the current position) so the plaintext is
    /// never fully resident in memory. The ENCRYPTED package (≈ the xlsx's compressed size) is
    /// buffered once for CFB container assembly.
    /// </summary>
    public static void WriteEncrypted(Stream output, Stream plaintextPackage, long length, string password, int spinCount = 100_000)
    {
        // 1. Encrypt the package, honoring the caller's spin count (KDF iteration count).
        using var encryptedPackageStream = new MemoryStream();
        var encParams = AgileEncryption.Encrypt(plaintextPackage, length, password, encryptedPackageStream, spinCount);
        var encryptedPackageBytes = encryptedPackageStream.ToArray();

        // 2. Build the EncryptionInfo XML stream.
        using var encryptionInfoStream = new MemoryStream();
        EncryptionInfoXml.Write(encryptionInfoStream, encParams);
        var encryptionInfoBytes = encryptionInfoStream.ToArray();

        // 3. Build the DataSpaces sub-tree.
        //    Tree structure (matches Excel's reference exactly):
        //      Root storage
        //      ├── EncryptedPackage (stream)
        //      ├── \x06DataSpaces (storage)
        //      │   ├── Version (stream)
        //      │   ├── DataSpaceMap (stream)
        //      │   ├── DataSpaceInfo (storage)
        //      │   │   └── StrongEncryptionDataSpace (stream)
        //      │   └── TransformInfo (storage)
        //      │       └── StrongEncryptionTransform (storage)
        //      │           └── \x06Primary (stream)
        //      └── EncryptionInfo (stream)

        var encryptionInfoNode = new CfbContainer.Node
        {
            Name = "EncryptionInfo",
            Type = CfbContainer.TypeStream,
            Data = encryptionInfoBytes,
            Color = CfbContainer.ColorBlack,
        };

        var encryptedPackageNode = new CfbContainer.Node
        {
            Name = "EncryptedPackage",
            Type = CfbContainer.TypeStream,
            Data = encryptedPackageBytes,
            Color = CfbContainer.ColorRed,
        };

        var versionNode = new CfbContainer.Node
        {
            Name = "Version",
            Type = CfbContainer.TypeStream,
            Data = DataSpacesBuilder.BuildVersionStream(),
            Color = CfbContainer.ColorBlack,
        };

        var dataSpaceMapNode = new CfbContainer.Node
        {
            Name = "DataSpaceMap",
            Type = CfbContainer.TypeStream,
            Data = DataSpacesBuilder.BuildDataSpaceMap(),
            Color = CfbContainer.ColorBlack,
        };

        var strongEncryptionDataSpaceNode = new CfbContainer.Node
        {
            Name = "StrongEncryptionDataSpace",
            Type = CfbContainer.TypeStream,
            Data = DataSpacesBuilder.BuildStrongEncryptionDataSpace(),
            Color = CfbContainer.ColorBlack,
        };

        var dataSpaceInfoNode = new CfbContainer.Node
        {
            Name = "DataSpaceInfo",
            Type = CfbContainer.TypeStorage,
            Color = CfbContainer.ColorBlack,
            Children = { strongEncryptionDataSpaceNode },
        };

        var primaryNode = new CfbContainer.Node
        {
            Name = "\u0006Primary",
            Type = CfbContainer.TypeStream,
            Data = DataSpacesBuilder.BuildPrimary(),
            Color = CfbContainer.ColorBlack,
        };

        var strongEncryptionTransformNode = new CfbContainer.Node
        {
            Name = "StrongEncryptionTransform",
            Type = CfbContainer.TypeStorage,
            Color = CfbContainer.ColorBlack,
            Children = { primaryNode },
        };

        var transformInfoNode = new CfbContainer.Node
        {
            Name = "TransformInfo",
            Type = CfbContainer.TypeStorage,
            Color = CfbContainer.ColorRed,
            Children = { strongEncryptionTransformNode },
        };

        var dataSpacesNode = new CfbContainer.Node
        {
            Name = "\u0006DataSpaces",
            Type = CfbContainer.TypeStorage,
            Color = CfbContainer.ColorRed,
            Children = { versionNode, dataSpaceMapNode, dataSpaceInfoNode, transformInfoNode },
        };

        // Root children: EncryptedPackage, \x06DataSpaces, EncryptionInfo
        // (matches Excel's directory entry order: EncryptedPackage=1, DataSpaces=2, EncryptionInfo=10)
        var rootChildren = new List<CfbContainer.Node>
        {
            encryptedPackageNode,
            dataSpacesNode,
            encryptionInfoNode,
        };

        CfbContainer.Write(output, rootChildren);
    }
}
