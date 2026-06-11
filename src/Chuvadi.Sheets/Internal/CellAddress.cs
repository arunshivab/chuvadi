using System;
using System.Globalization;
using System.Text;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Utility for converting between Excel-style cell addresses ("A1", "Z99", "AA1", "BC1234")
/// and (row, column) integer pairs. Column numbers are 1-based to match Excel's convention:
/// A=1, B=2, ..., Z=26, AA=27, ..., XFD=16384 (Excel's max column).
///
/// Hot path in writers: every cell needs its A1 address. This implementation avoids
/// allocations for the common case (columns 1..702, which covers A..ZZ).
/// </summary>
internal static class CellAddress
{
    /// <summary>Excel's max columns (XFD).</summary>
    public const int MaxColumn = 16384;

    /// <summary>Excel's max rows (1,048,576).</summary>
    public const int MaxRow = 1048576;

    /// <summary>
    /// Encodes a 1-based column index as Excel letters. 1→"A", 26→"Z", 27→"AA", 702→"ZZ", 703→"AAA".
    /// </summary>
    public static string ColumnLetters(int column)
    {
        if (column < 1 || column > MaxColumn)
            throw new ArgumentOutOfRangeException(
                nameof(column), $"Column index must be 1..{MaxColumn} (got {column}).");

        // Excel's base-26 is "bijective" — there is no zero digit. A=1..Z=26, then AA=27.
        // The conversion is repeated (col-1) divmod 26, prepending letters.
        Span<char> buffer = stackalloc char[3]; // Enough for up to ZZZ (column 18278); XFD fits.
        int pos = buffer.Length;
        int c = column;
        while (c > 0)
        {
            c--; // shift to 0-based for divmod
            buffer[--pos] = (char)('A' + (c % 26));
            c /= 26;
        }
        return new string(buffer.Slice(pos));
    }

    /// <summary>
    /// Returns the A1-style address for a cell at (1-based row, 1-based column).
    /// </summary>
    public static string ToA1(int row, int column)
    {
        if (row < 1 || row > MaxRow)
            throw new ArgumentOutOfRangeException(
                nameof(row), $"Row index must be 1..{MaxRow} (got {row}).");
        return ColumnLetters(column) + row.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses an A1-style address into (row, column). Both 1-based.
    /// </summary>
    public static (int Row, int Column) ParseA1(string address)
    {
        if (string.IsNullOrEmpty(address))
            throw new ArgumentException("Address cannot be null or empty.", nameof(address));

        int i = 0;
        int column = 0;
        while (i < address.Length && address[i] >= 'A' && address[i] <= 'Z')
        {
            column = column * 26 + (address[i] - 'A' + 1);
            i++;
        }
        if (column == 0)
            throw new FormatException($"Address '{address}' is missing column letters.");

        int row = 0;
        while (i < address.Length && address[i] >= '0' && address[i] <= '9')
        {
            row = row * 10 + (address[i] - '0');
            i++;
        }
        if (row == 0)
            throw new FormatException($"Address '{address}' is missing a row number.");
        if (i != address.Length)
            throw new FormatException($"Address '{address}' has trailing characters after the row number.");

        return (row, column);
    }
}
