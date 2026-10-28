using System;

namespace Chuvadi.Markup
{
    /// <summary>
    /// Thrown when input would make a Chuvadi.Markup call exceed one of the <see cref="MarkupLimits"/>
    /// that cannot be degraded gracefully. The call has produced no partial result.
    /// </summary>
    public sealed class MarkupLimitExceededException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public MarkupLimitExceededException(string limitName, long limit, long actual)
            : base("Markup limit '" + limitName + "' exceeded: limit " + limit + ", reached " + actual + ".")
        {
            LimitName = limitName;
            Limit = limit;
            Actual = actual;
        }

        /// <summary>Name of the <see cref="MarkupLimits"/> property that was exceeded.</summary>
        public string LimitName { get; }

        /// <summary>The configured limit.</summary>
        public long Limit { get; }

        /// <summary>The value reached when the limit was detected (at least <see cref="Limit"/> + 1).</summary>
        public long Actual { get; }
    }
}
