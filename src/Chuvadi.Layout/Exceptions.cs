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

        /// <summary>Name of the <see cref="LayoutLimits"/> property that was exceeded.</summary>
        public string LimitName { get; }

        /// <summary>The configured limit.</summary>
        public long Limit { get; }

        /// <summary>The value that was reached when the limit tripped.</summary>
        public long Actual { get; }
    }
}
