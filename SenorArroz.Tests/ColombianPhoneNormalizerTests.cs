using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public sealed class ColombianPhoneNormalizerTests
{
    [Theory]
    [InlineData("3001234567")]
    [InlineData("+57 300 123 4567")]
    [InlineData("573001234567")]
    public void EquivalentFormatsResolveToNationalIdentity(string input)
    {
        Assert.True(ColombianPhoneNormalizer.TryNormalize(input, out var normalized));
        Assert.Equal("3001234567", normalized);
    }

    [Theory]
    [InlineData("6041234567")]
    [InlineData("300123456")]
    [InlineData("5730012345678")]
    [InlineData("")]
    public void InvalidMobileIsRejected(string input) =>
        Assert.False(ColombianPhoneNormalizer.TryNormalize(input, out _));

    [Fact]
    public void AdministrativeFixedLineIsAcceptedOnlyAsCustomerContact()
    {
        Assert.True(ColombianPhoneNormalizer.TryNormalizeAdministrativeFixedLine("604 123 4567", out var fixedLine));
        Assert.Equal("6041234567", fixedLine);
        Assert.True(ColombianPhoneNormalizer.TryNormalizeCustomerContact("6041234567", out _));
        Assert.False(ColombianPhoneNormalizer.TryNormalize("6041234567", out _));
    }
}
