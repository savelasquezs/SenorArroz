using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SenorArroz.API.Hosting;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Options;

namespace SenorArroz.Tests;

public sealed class WhatsAppProcessingTests
{
    internal static string Payload(string id = "wamid.1", string phone = "573000000001") => JsonSerializer.Serialize(new
    {
        @object = "whatsapp_business_account",
        entry = new[] { new { id = "business", changes = new[] { new { field = "messages", value = new
        {
            metadata = new { phone_number_id = "channel-1" },
            messages = new[] { new { id, from = phone, timestamp = "1793400000", type = "text", text = new { body = "Hola" } } }
        } } } } }
    });

    [Fact]
    public void Splitting_preserves_identity_and_deduplicates_same_message()
    {
        var first = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload()));
        var repeated = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload()));
        var next = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("wamid.2")));
        var other = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("wamid.3", "573000000002")));
        Assert.Equal(first.Key, repeated.Key);
        Assert.NotEqual(first.Key, next.Key);
        Assert.Equal(first.Partition, next.Partition);
        Assert.NotEqual(first.Partition, other.Partition);
        Assert.DoesNotContain("573000000001", first.Partition);
        using var document = JsonDocument.Parse(first.Payload);
        Assert.Equal("channel-1", document.RootElement.GetProperty("entry")[0].GetProperty("changes")[0]
            .GetProperty("value").GetProperty("metadata").GetProperty("phone_number_id").GetString());
    }

    [Fact]
    public void Worker_requires_external_mode_and_private_business_credentials()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .ConfigureWhatsAppProcessing(new ConfigurationBuilder().Build(), worker: true));
        var settings = new Dictionary<string, string?>
        {
            ["WhatsAppProcessing:Mode"] = "External", ["WhatsAppProcessing:WorkerKey"] = new string('x', 32),
            ["WhatsAppProcessing:BusinessApiUrl"] = "http://public.example.com"
        };
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .ConfigureWhatsAppProcessing(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), true));
    }

    [Fact]
    public void Api_and_worker_register_distinct_consumers()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppProcessing:Mode"] = "External", ["WhatsAppProcessing:WorkerKey"] = new string('x', 32),
            ["WhatsAppProcessing:BusinessApiUrl"] = "http://127.0.0.1:8080"
        }).Build();
        var api = new ServiceCollection();
        api.AddHostedService<WhatsAppAiBackgroundService>();
        api.ConfigureWhatsAppProcessing(configuration, false);
        Assert.DoesNotContain(api, x => x.ImplementationType == typeof(WhatsAppAiBackgroundService));
        Assert.Contains(api, x => x.ImplementationType == typeof(WhatsAppRealtimeRelay));
        var worker = new ServiceCollection();
        worker.ConfigureWhatsAppProcessing(configuration, true);
        Assert.Contains(worker, x => x.ImplementationType == typeof(WhatsAppWebhookWorker));
        Assert.DoesNotContain(worker, x => x.ImplementationType == typeof(WhatsAppRealtimeRelay));
        Assert.All(worker.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType is not null), x =>
            Assert.StartsWith("WhatsApp", x.ImplementationType!.Name));
    }

    [Fact]
    public void Lease_must_exceed_handler_timeout()
    {
        Assert.False(new WhatsAppProcessingOptions { LeaseSeconds = 60, WorkTimeoutSeconds = 120 }.IsValid());
        Assert.True(new WhatsAppProcessingOptions().IsValid());
        Assert.False(new WhatsAppProcessingOptions { AiConcurrency = 0 }.IsValid());
    }
}
