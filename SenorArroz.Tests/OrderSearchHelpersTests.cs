using SenorArroz.Infrastructure.Common;

namespace SenorArroz.Tests;

public class OrderSearchHelpersTests
{
    [Theory]
    [InlineData("30", 30)]
    [InlineData("30", 305)]
    [InlineData("3", 3)]
    [InlineData("3", 35)]
    public void OrderTotalPrefixRanges_ContainsTotal(string prefix, int total)
    {
        var ranges = OrderTotalPrefixRanges.BuildRanges(prefix);
        Assert.NotEmpty(ranges);
        Assert.Contains(ranges, r => total >= r.Min && total <= r.Max);
    }

    [Fact]
    public void OrderTotalPrefixRanges_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Empty(OrderTotalPrefixRanges.BuildRanges(""));
        Assert.Empty(OrderTotalPrefixRanges.BuildRanges("abc"));
    }

    [Fact]
    public void SqlSearchPattern_EscapeForLike_EscapesPercent()
    {
        Assert.Contains("\\%", SqlSearchPattern.EscapeForLike("100%"));
        Assert.Contains("\\_", SqlSearchPattern.EscapeForLike("a_b"));
    }

    [Fact]
    public void SqlSearchPattern_ILikeContains_WrapsEscaped()
    {
        var p = SqlSearchPattern.ILikeContains("a");
        Assert.StartsWith("%", p);
        Assert.EndsWith("%", p);
    }
}
