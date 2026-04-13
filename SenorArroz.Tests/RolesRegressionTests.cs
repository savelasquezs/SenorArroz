using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

public class RolesRegressionTests
{
    [Fact]
    public void Role_constants_match_jwt_claim_convention()
    {
        Assert.Equal("superadmin", Roles.Superadmin);
        Assert.Equal("admin", Roles.Admin);
        Assert.Equal("cashier", Roles.Cashier);
        Assert.Equal("kitchen", Roles.Kitchen);
        Assert.Equal("deliveryman", Roles.Deliveryman);
    }

    [Theory]
    [InlineData("SUPERADMIN", "superadmin", true)]
    [InlineData("Superadmin", "superadmin", true)]
    [InlineData("admin", "superadmin", false)]
    [InlineData("Cashier", "cashier", true)]
    public void EqualsOrdinalIgnoreCase_matches_expected(string a, string b, bool expected)
    {
        Assert.Equal(expected, Roles.EqualsOrdinalIgnoreCase(a, b));
    }

    [Theory]
    [InlineData("ADMIN", true)]
    [InlineData("admin", true)]
    [InlineData("superadmin", false)]
    public void IsAdmin_ignores_case(string role, bool expected)
    {
        Assert.Equal(expected, Roles.IsAdmin(role));
    }
}
