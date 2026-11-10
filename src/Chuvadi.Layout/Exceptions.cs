using System;

namespace Chuvadi.Layout
{
    /// <summary>Base type of every exception Chuvadi.Layout throws on purpose.</summary>
    public class LayoutException : Exception
    {
        public LayoutException(string message) : base(message) { }
        public LayoutException(string message, Exception? innerException) : base(message, innerException) { }
    }

    /// <summary>The input (document, font, image) is malformed or not supported.</summary>
    public class LayoutFormatException : LayoutException
    {
        public LayoutFormatException(string message) : base(message) { }
        public LayoutFormatException(string message, Exception? innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// The input is an encrypted Office file and <see cref="LayoutOptions.Password"/> is missing or wrong (D-253). A
    /// <see cref="LayoutFormatException"/>, so existing handlers still catch it. Thrown by DocsLayout.Open,
    /// SheetsLayout.Open and AutoDetectLayout.Open; <see cref="Exception.InnerException"/> is the Chuvadi.Docs or
    /// Chuvadi.Sheets password exception.
    /// </summary>
    /// <remarks>
    /// Only a compound file with the EncryptionInfo and EncryptedPackage streams is treated as encrypted (D-293). A
    /// legacy .doc or .xls (not supported) is never reported here: it is a plain <see cref="LayoutFormatException"/>
    /// whose message names the old format. A compound file whose directory cannot be read may still be a damaged
    /// encrypted file, so without a password it still asks for one; with a password, Chuvadi.Docs (2.1.0) and
    /// Chuvadi.Sheets report the damaged container as a format error, so it is a plain <see cref="LayoutFormatException"/>
    /// (D-300, D-305).
    /// </remarks>
    public sealed class LayoutPasswordRequiredException : LayoutFormatException
    {
        public LayoutPasswordRequiredException(string message, bool passwordGiven, Exception? innerException)
            : base(message, innerException)
        {
            PasswordGiven = passwordGiven;
        }

        /// <summary>
        /// True when <see cref="LayoutOptions.Password"/> was set (not null or empty), so the password given is wrong;
        /// false when none was given.
        /// </summary>
        public bool PasswordGiven { get; }
    }

    /// <summary>A font program is malformed, truncated or of an unsupported kind.</summary>
    public sealed class FontFormatException : LayoutFormatException
    {
        public FontFormatException(string message) : base(message) { }
        public FontFormatException(string message, Exception? innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// A configured <see cref="LayoutLimits"/> value was exceeded. Thrown before the
    /// allocation or work that would exceed it, so untrusted input cannot exhaust memory or time.
    /// </summary>
    public sealed class LayoutLimitExceededException : LayoutException
    {
        public LayoutLimitExceededException(string limitName, long limit, long actual)
            : base($"Layout limit '{limitName}' exceeded: limit {limit}, required at least {actual}.")
        {
            LimitName = limitName;
            Limit = limit;
            Actual = actual;
        }

        /// <summary>
        /// A limit exceeded at a known place in the document (D-291): <paramref name="location"/> names it, for example
        /// "Sheet1!A1:XFD1048576", and <paramref name="message"/> explains it.
        /// </summary>
        public LayoutLimitExceededException(string limitName, long limit, long actual, string message, string? location)
            : base(message)
        {
            LimitName = limitName;
            Limit = limit;
            Actual = actual;
            Location = location;
        }

        /// <summary>Name of the <see cref="LayoutLimits"/> property that was exceeded.</summary>
        public string LimitName { get; }

        /// <summary>The configured limit.</summary>
        public long Limit { get; }

        /// <summary>
        /// The value that was reached when the limit tripped. For <see cref="LayoutLimits.MaxPages"/> on a workbook, the
        /// workbook's whole page count.
        /// </summary>
        public long Actual { get; }

        /// <summary>
        /// Where in the document the limit was exceeded, when known: for <see cref="LayoutLimits.MaxPages"/> on a workbook,
        /// the sheet and printed range of the page that was asked for ("Sheet1!A1:XFD1048576"). Null otherwise.
        /// </summary>
        public string? Location { get; }
    }
}
