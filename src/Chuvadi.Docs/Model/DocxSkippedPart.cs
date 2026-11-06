using System;

namespace Chuvadi.Docs.Model;

/// <summary>Why <see cref="DocxReader"/> skipped a part (see <see cref="DocxReader.SkippedParts"/>).</summary>
public enum DocxSkippedPartReason
{
    /// <summary>The package refers to the part, but it is not in the package.</summary>
    Missing,

    /// <summary>The part is in the package but could not be opened (for example a corrupt zip entry header).</summary>
    Unreadable,

    /// <summary>The part is not well-formed XML. A header or footer like this reads as blank, as in <see cref="Document.Load(System.IO.Stream, string?)"/>.</summary>
    NotWellFormed,
}

/// <summary>
/// One optional part that <see cref="DocxReader"/> skipped while reading: a header, a footer or a
/// picture. Nothing is dropped silently: every skip is listed in <see cref="DocxReader.SkippedParts"/>.
/// The main document part is never skipped; a problem there is a <see cref="DocxFormatException"/>.
/// </summary>
public sealed class DocxSkippedPart
{
    internal DocxSkippedPart(string partName, DocxSkippedPartReason reason, Exception? error)
    {
        PartName = partName;
        Reason = reason;
        Error = error;
    }

    /// <summary>The part's name in the package, for example <c>/word/header1.xml</c>.</summary>
    public string PartName { get; }

    /// <summary>Why it was skipped.</summary>
    public DocxSkippedPartReason Reason { get; }

    /// <summary>The error that made the part unusable; null when the part is missing.</summary>
    public Exception? Error { get; }

    /// <inheritdoc/>
    public override string ToString() => Error is null ? $"{PartName}: {Reason}" : $"{PartName}: {Reason} ({Error.Message})";
}
