using Chuvadi.Print;

namespace Chuvadi.Print.Tests;

public class PageSelectionTests
{
    [Theory]
    [InlineData("1-3, 5", 10, new[] { 1, 2, 3, 5 })]
    [InlineData("7", 10, new[] { 7 })]
    [InlineData("9-11", 10, new[] { 9, 10 })]          // clamps to page count
    [InlineData(" 2 - 4 , 2 ", 10, new[] { 2, 3, 4 })]  // whitespace + dedupe
    public void Range_resolves_expected(string spec, int pageCount, int[] expected)
    {
        Assert.Equal(expected, PageSelection.Range(spec).Resolve(pageCount));
    }

    [Theory]
    [InlineData("0-3")]
    [InlineData("5-2")]
    [InlineData("abc")]
    [InlineData("3-")]
    public void Range_rejects_invalid(string spec)
    {
        Assert.Throws<FormatException>(() => PageSelection.Range(spec));
    }

    [Fact]
    public void Explicit_rejects_non_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageSelection.Explicit(1, 0, 3));
    }
}
