using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Helpers;

namespace SenorArroz.Tests;

public sealed class ResendWebhookTests
{
    private const string Secret = "whsec_plJ3nmyCDGBKInavdOK15jsl";
    private const string Body = """{"event_type":"ping","data":{"success":true}}""";
    private const string Signature = "v1,rAvfW3dJ/X/qxhsaXPOyyCGmRKsaKWcsNccKXlIktD0=";
    private static DateTime Now => DateTimeOffset.FromUnixTimeSeconds(1731705121).UtcDateTime;

    [Fact]
    public void Official_Svix_vector_accepts_rotating_signatures_and_rejects_tampering_replay_and_missing_secret()
    {
        bool Verify(string body = Body, string id = "msg_loFOjxBNrRLzqYUf", string timestamp = "1731705121", string signature = Signature, string? secret = Secret, DateTime? now = null)
            => ResendWebhook.Verify(body, id, timestamp, signature, secret, now ?? Now);
        Assert.True(Verify());
        Assert.True(Verify(signature: "v1,invalid " + Signature));
        Assert.False(Verify(body: Body + " ")); Assert.False(Verify(id: "another-message"));
        Assert.False(Verify(now: Now.AddSeconds(301))); Assert.False(Verify(now: Now.AddSeconds(-301)));
        Assert.False(Verify(signature: "v2," + Signature[3..])); Assert.False(Verify(secret: null));
        Assert.False(Verify(secret: "invalid")); Assert.False(Verify(timestamp: "9223372036854775807"));
    }

    [Theory]
    [InlineData("email.delivered", "delivered")]
    [InlineData("email.bounced", "bounced")]
    [InlineData("email.delivery_delayed", "delayed")]
    [InlineData("email.failed", "failed")]
    [InlineData("email.suppressed", "suppressed")]
    [InlineData("email.complained", "complained")]
    public void Reads_provider_event_time_id_recipient_and_bounce_reason(string type, string status)
    {
        var result = ResendWebhook.Read("""{"type":"EVENT","created_at":"2026-10-23T02:00:00Z","data":{"email_id":"56761188-7520-42d8-8898-ff6fc54ce618","to":["client@example.com"],"bounce":{"message":"Mailbox unavailable"},"tags":{"outbox_id":"12"}}}""".Replace("EVENT", type))!;
        Assert.Equal(status, result.Status); Assert.Equal(12, result.OutboxId);
        Assert.Equal(DateTimeKind.Utc, result.OccurredAt.Kind); Assert.Equal("Mailbox unavailable", result.Error);
        Assert.Equal("client@example.com", Assert.Single(result.Recipients));
        Assert.Null(ResendWebhook.Read("""{"type":"email.opened"}"""));
        Assert.False(ResendWebhook.CanAdvance("delivered", result.OccurredAt.AddSeconds(1), result));
    }

    [Theory]
    [InlineData("delivered", "delayed")]
    [InlineData("bounced", "delivered")]
    [InlineData("complained", "delivered")]
    public void Later_intermediate_events_do_not_replace_terminal_delivery_results(string current, string next)
    {
        Assert.False(ResendWebhook.CanAdvance(current, Now, new("id", next, Now.AddMinutes(1), [], null, null)));
    }

    [Fact]
    public async Task Unsigned_request_is_rejected_before_any_database_or_tenant_access()
    {
        var execution = new Mock<ITenantExecutionContext>(MockBehavior.Strict);
        var clock = new Mock<IClock>(); clock.SetupGet(x => x.UtcNow).Returns(Now);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ResendSettings:WebhookSecret"] = Secret }).Build();
        var controller = new ResendWebhooksController(null!, execution.Object, config, clock.Object)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body));
        Assert.IsType<UnauthorizedResult>(await controller.Receive(default));
        execution.VerifyNoOtherCalls();
    }
}
