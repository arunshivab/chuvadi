using System;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Model;

/// <summary>
/// A1-style cell references (since 2.1.0): the conversions Chuvadi.Sheets uses itself, with the same results and
/// exceptions. Rows and columns are 1-based, as in Excel: column 1 is "A", 16,384 is "XFD"; row 1,048,576 is the
/// last row.
/// </summary>
public static class CellReference
{
    /// <summary>The column letters of a 1-based column number: 1 → "A", 27 → "AA", 16,384 → "XFD".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="column"/> is not 1 to 16,384.</exception>
    public static string ColumnLetters(int column) => CellAddress.ColumnLetters(column);

    /// <summary>
    /// The 1-based column number of column letters: "A" → 1, "AA" → 27, "XFD" → 16,384. The letters are
    /// upper-case A to Z only, as Excel writes them in a file; "$" and lower case are not accepted.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="letters"/> is null or empty.</exception>
    /// <exception cref="FormatException"><paramref name="letters"/> holds a character other than A to Z.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The letters name a column past "XFD".</exception>
    public static int ColumnIndex(string letters)
    {
        if (string.IsNullOrEmpty(letters))
            throw new ArgumentException("Column letters cannot be null or empty.", nameof(letters));
        int column = 0;
        foreach (char ch in letters)
        {
            if (ch < 'A' || ch > 'Z')
                throw new FormatException($"Column letters '{letters}' may hold only A to Z.");
            column = column * 26 + (ch - 'A' + 1);
            if (column > CellAddress.MaxColumn)
                throw new ArgumentOutOfRangeException(nameof(letters), letters, $"Column letters must be A to XFD (1..{CellAddress.MaxColumn}).");
        }
        return column;
    }

    /// <summary>The A1 reference of a cell: (1, 1) → "A1", (10, 28) → "AB10".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> is not 1 to 1,048,576, or
    /// <paramref name="column"/> is not 1 to 16,384.</exception>
    public static string ToA1(int row, int column) => CellAddress.ToA1(row, column);

    /// <summary>
    /// The 1-based row and column of an A1 reference: "B3" → (3, 2). The reference is upper-case column letters
    /// followed by the row number, as Excel writes them in a file. It checks the shape, not the range: a row past
    /// 1,048,576 or a column past "XFD" comes back as it is written, so compare with those limits where it matters.
    /// "$" (an absolute reference such as "$B$3"), lower case, spaces and ranges ("A1:B2") are not accepted.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="address"/> is null or empty.</exception>
    /// <exception cref="FormatException">The column letters or the row number are missing, or characters follow
    /// the row number.</exception>
    public static (int Row, int Column) ParseA1(string address) => CellAddress.ParseA1(address);
}
