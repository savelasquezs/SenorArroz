using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public class IntegrationSecretProtectorTests
{
    [Fact]
    public void RoundTripEncryptsAndDoesNotExposeSecret()
    {
        var protector = Create("stable-production-key");
        var encrypted = protector.Protect("rappi-client-secret");

        Assert.StartsWith("v1.", encrypted);
        Assert.DoesNotContain("rappi-client-secret", encrypted);
        Assert.Equal("rappi-client-secret", protector.Unprotect(encrypted));
    }

    [Fact]
    public void DifferentKeyCannotDecrypt()
    {
        var encrypted = Create("first-key").Protect("secret");
        Assert.ThrowsAny<CryptographicException>(() => Create("second-key").Unprotect(encrypted));
    }

    [Fact]
    public void MissingKeyFailsBeforePersistingCredentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.Throws<InvalidOperationException>(() => new IntegrationSecretProtector(configuration).Protect("secret"));
    }

    private static IntegrationSecretProtector Create(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integrations:EncryptionKey"] = key
        }).Build();
        return new IntegrationSecretProtector(configuration);
    }
}
