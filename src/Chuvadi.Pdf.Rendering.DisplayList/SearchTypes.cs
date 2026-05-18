// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — text search

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Options controlling a search.</summary>
public sealed class SearchOptions
{
    /// <summary>Match case-sensitively. Default false.</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>Require whole-word matches. Default false.</summary>
    public bool WholeWord { get; init; }

    /// <summary>Optional inclusive start page (0-based). Default 0.</summary>
    public int? PageRangeStart { get; init; }

    /// <summary>Optional exclusive end page (0-based). Default = page count.</summary>
    public int? PageRangeEnd { get; init; }
}

/// <summary>A search match against the logical text of a page.</summary>
public sealed class SearchMatch
{
    /// <summary>Initialises a search match.</summary>
    public SearchMatch(int pageNumber, int characterOffset, int length, IReadOnlyList<Rect> boundingBoxes)
    {
        PageNumber = pageNumber;
        CharacterOffset = characterOffset;
        Length = length;
        BoundingBoxes = boundingBoxes;
    }

    /// <summary>Zero-based page index.</summary>
    public int PageNumber { get; }

    /// <summary>Character offset within the page's logical concatenated text.</summary>
    public int CharacterOffset { get; }

    /// <summary>Match length in characters.</summary>
    public int Length { get; }

    /// <summary>Bounding boxes (multiple if the match spans more than one text run).</summary>
    public IReadOnlyList<Rect> BoundingBoxes { get; }
}
