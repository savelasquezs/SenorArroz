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

    [Theory]
    [InlineData(
        "{\"store_id\":\"900173116\",\"online\":true,\"checked_at\":\"2026-08-14 16:06:04\"}",
        "06167f7da66e6fc61ad322b640973b622b8317234483d4966807f4b723c089c1")]
    [InlineData(
        "{\"order_id\":\"test-order\",\"total_order\":31000.00,\"delivery_information\":null,\"takeaway\":false}",
        "e866b30f8d2cb7f87d5b89b3a776513cab10e668be4aa1e35f37d7af8896d12d")]
    public void IsValid_AcceptsSandboxTesterCanonicalPayload(string payload, string signature)
    {
        Assert.True(RappiWebhookSignature.IsValid(
            $"t=1786723564421,sign={signature}",
            payload,
            "event-secret", allowSandboxTesterPayload: true));
        Assert.False(RappiWebhookSignature.IsValid(
            $"t=1786723564421,sign={signature}", payload, "event-secret"));
    }

    [Fact]
    public void Synthetic_vector_checked_in_Rappi_portal_matches_strict_raw_payload_validation()
    {
        // Checked in the DEV portal's 100% client-side verifier on 2026-10-21.
        // Deliberately synthetic secret/payload; not a credential or received webhook.
        const string secret = "rappi-readiness-test-only";
        const string payload = """{"store_id":"audit-local","quantity":2,"enabled":false,"note":null}""";
        const string rawSignature = "0234b3cb73c82ae2fd95cfc358a437e7883b349dd1eafe16a64b7143cdf6c991";
        const string normalizedSignature = "841ea79ef2e531b58d85439879c6e9d36b1f5d2052ff9f97adc337a84b246838";
        Assert.True(RappiWebhookSignature.IsValid($"t=1792594800000,sign={rawSignature}", payload, secret));
        Assert.False(RappiWebhookSignature.IsValid($"t=1792594800000,sign={normalizedSignature}", payload, secret));
        Assert.False(RappiWebhookSignature.IsValid($"t=1792594800000,sign={rawSignature}", payload.Replace("2", "3"), secret));
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
