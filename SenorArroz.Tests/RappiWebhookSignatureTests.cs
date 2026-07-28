using System.Security.Cryptography;
using System.Text;
using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public sealed class RappiWebhookSignatureTests
{
    [Fact]
    public void IsValid_UsesTimestampDotRawPayload()
    {
        const string timestamp = "123456";
        const string payload = "{ \"store_id\" : 900173116 }";
        const string secret = "event-secret";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}")))
            .ToLowerInvariant();

        Assert.True(RappiWebhookSignature.IsValid(
            $"t={timestamp},sign={signature}",
            payload,
            secret));
        Assert.Equal(timestamp, RappiWebhookSignature.GetTimestamp(
            $"t={timestamp},sign={signature}"));
    }

    [Fact]
    public void IsValid_RejectsModifiedPayloadAndMalformedHeaders()
    {
        const string header =
            "t=123456,sign=d74b65c2e68c1a84a4d5843a69ef5faf1d82f28df2dd3723e8e0dad9c54abc79";

        Assert.False(RappiWebhookSignature.IsValid(header, "{}", "secret"));
        Assert.False(RappiWebhookSignature.IsValid("sign=abc", "{}", "secret"));
        Assert.False(RappiWebhookSignature.IsValid(null, "{}", "secret"));
    }
}
