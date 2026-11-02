namespace Chuvadi.Layout.Markup
{
    internal static class Limits
    {
        /// <summary>Throws <see cref="LayoutLimitExceededException"/> when <paramref name="actual"/> exceeds <paramref name="limit"/>.</summary>
        public static void Check(string name, long limit, long actual)
        {
            if (actual > limit)
            {
                throw new LayoutLimitExceededException(name, limit, actual);
            }
        }
    }
}
