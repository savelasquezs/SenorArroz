using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.API.Controllers;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class WhatsAppAiObservabilityHardeningTests
{
    [Theory]
    [InlineData("openai", 100, 0, 20, 0, 100, 0, 20)]
    [InlineData("openai", 100, 25, 20, 10, 75, 25, 20)]
    [InlineData("gemini", 100, 0, 20, 0, 100, 0, 20)]
    [InlineData("gemini", 100, 0, 20, 10, 100, 0, 30)]
    [InlineData("gemini", 100, 25, 20, 10, 75, 25, 30)]
    public void Billing_usage_has_provider_specific_output_semantics(string provider, int input, int cached, int output, int thinking, int expectedUncached, int expectedCached, int expectedBillableOutput)
    {
        var response = new AiChatResponse(null, [], "model", null, input, output, CachedInputTokens: cached, ThinkingTokens: thinking);
        var usage = AiBillingUsage.From(provider, response);
        Assert.Equal(expectedUncached, usage.UncachedInputTokens);
        Assert.Equal(expectedCached, usage.CachedInputTokens);
        Assert.Equal(expectedBillableOutput, usage.BillableOutputTokens);
        Assert.Equal(output, usage.VisibleOutputTokens);
        Assert.Equal(thinking, usage.ThinkingTokens);
    }

    [Fact]
    public void Colombia_midnight_converts_to_five_am_utc()
    {
        Assert.Equal(new DateTime(2026, 7, 13, 5, 0, 0, DateTimeKind.Utc), WhatsAppAiUsageController.ToUtc(new DateOnly(2026, 7, 13)));
    }

    [Fact]
    public void Bounded_queue_rejects_without_waiting_when_full()
    {
        var queue = new WhatsAppAiTelemetryQueue(NullLogger<WhatsAppAiTelemetryQueue>.Instance);
        for (var i = 0; i < 1000; i++) Assert.True(queue.TryEnqueue(new WhatsAppAiInvocation { Provider = "openai", Model = "m" }));
        Assert.False(queue.TryEnqueue(new WhatsAppAiInvocation { Provider = "openai", Model = "m" }));
    }
}
