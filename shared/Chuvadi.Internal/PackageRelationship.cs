using System;

namespace Chuvadi.Internal;

/// <summary>
/// Represents one relationship from a source part to a target part within an OOXML package.
/// Relationships are stored in .rels XML files alongside their source part. The package root
/// has its relationships in "/_rels/.rels"; a part at "/xl/workbook.xml" has its relationships
/// in "/xl/_rels/workbook.xml.rels".
/// </summary>
internal sealed class PackageRelationship
{
    /// <summary>
    /// The relationship ID. Must be unique within the set of relationships sharing the same
    /// source part. Conventionally "rId1", "rId2", etc. Cells and other content reference
    /// these IDs to point at other parts.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The OOXML relationship type URI. Examples:
    ///   - http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument
    ///   - http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet
    /// These strings are part of the OOXML standard and must match exactly.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// The target part's path, relative to the source part's directory.
    /// Stored as written in the .rels file, e.g. "worksheets/sheet1.xml" for a workbook
    /// relationship targeting "/xl/worksheets/sheet1.xml".
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// True if this relationship targets an external resource (URL) rather than a part inside
    /// the package. External relationships get <c>TargetMode="External"</c> in the .rels file
    /// and their Target is preserved verbatim (no relative-path computation).
    /// </summary>
    public bool IsExternal { get; }

    public PackageRelationship(string id, string type, string target, bool isExternal = false)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Relationship ID cannot be null or empty.", nameof(id));
        if (string.IsNullOrEmpty(type))
            throw new ArgumentException("Relationship type cannot be null or empty.", nameof(type));
        if (string.IsNullOrEmpty(target))
            throw new ArgumentException("Relationship target cannot be null or empty.", nameof(target));

        Id = id;
        Type = type;
        Target = target;
        IsExternal = isExternal;
    }
}
