using Chuvadi.Docs.Model;
using Chuvadi.Markup;

namespace Chuvadi.Docs.Conversion.Internal;

/// <summary>
/// Reports an input-limit breach from any importer as the one Docs type,
/// <see cref="DocxLimitExceededException"/> (Docs 2.0, D-154, D-168), with Markup's original
/// error as InnerException.
/// </summary>
internal static class InputLimits
{
    public static DocxLimitExceededException From(MarkupLimitExceededException e, string input)
        => new(e.LimitName, e.Limit, e.Actual, $"the {input} input is beyond the importer's limits.", e);
}
