using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SenorArroz.API.Security;

namespace SenorArroz.Tests;

public class StorefrontApiKeyValidatorTests
{
    [Fact]
    public void IsValid_AcceptsOnlyConfiguredIdentifierAndKey()
    {
        const string key = "storefront-secret";
        var validator = new StorefrontApiKeyValidator(Options.Create(new StorefrontApiKeyOptions
        {
            KeyId = "web-main",
            KeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
        }));

        Assert.True(validator.IsValid("web-main", key));
        Assert.False(validator.IsValid("web-main", "wrong"));
        Assert.False(validator.IsValid("other", key));
    }

    [Fact]
    public void IsValid_RejectsMissingOrMalformedConfiguration()
    {
        var missing = new StorefrontApiKeyValidator(Options.Create(new StorefrontApiKeyOptions()));
        var malformed = new StorefrontApiKeyValidator(Options.Create(new StorefrontApiKeyOptions
        {
            KeyId = "web-main",
            KeyHash = "not-hex"
        }));

        Assert.False(missing.IsValid("web-main", "key"));
        Assert.False(malformed.IsValid("web-main", "key"));
    }
}
