using System;

namespace Chuvadi.OpenType;

/// <summary>
/// Thrown when font data cannot be used at all: the container header or table
/// directory is unreadable, or a table every reader needs ('head', 'maxp') is
/// missing or unusable. Damage anywhere else never throws; that data reads as empty.
/// </summary>
public class OpenTypeFormatException : FormatException
{
    /// <summary>Creates the exception with a default message.</summary>
    public OpenTypeFormatException()
        : base("The data is not a usable OpenType font.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What is wrong with the font data.</param>
    public OpenTypeFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What is wrong with the font data.</param>
    /// <param name="innerException">The underlying cause.</param>
    public OpenTypeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
