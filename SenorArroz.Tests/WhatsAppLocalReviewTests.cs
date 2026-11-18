using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Hosting;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.WhatsApp;

namespace SenorArroz.Tests;

public sealed class WhatsAppLocalReviewTests
{
    private static ServiceProvider LocalProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("provider");
        services.ConfigureWhatsAppLocalReview(Configuration(true),
            Mock.Of<IHostEnvironment>(value => value.EnvironmentName == "Development"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Local_text_and_summary_use_distinct_simulated_receipts_and_real_line_stays_blocked()
    {
        await using var provider = LocalProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("provider");
        var client = new WhatsAppCloudClient(http, Options.Create(new WhatsAppCloudOptions()), NullLogger<WhatsAppCloudClient>.Instance);
        var text = await client.SendTextMessageAsync("local-coexistence-review", "local-disabled", "573000000000", "Respuesta local");
        var summary = await client.SendTextMessageAsync("local-coexistence-review", "local-disabled", "573000000000", "Resumen: carbonara Dúo, recoge en Manrique, $33.000");
        Assert.True(text.Success);
        Assert.True(summary.Success);
        Assert.StartsWith("wamid.local.simulation.sent.", text.WhatsAppMessageId);
        Assert.StartsWith("wamid.local.simulation.sent.", summary.WhatsAppMessageId);
        Assert.NotEqual(text.WhatsAppMessageId, summary.WhatsAppMessageId);
        var realLine = await client.SendTextMessageAsync("123456789", "local-disabled", "573000000000", "Bloqueado");
        Assert.False(realLine.Success);
        Assert.Contains("deshabilitados", realLine.ErrorMessage);
    }

    [Theory]
    [InlineData("https://graph.facebook.com/v20.0/123/messages", "local-disabled", "text")]
    [InlineData("https://graph.facebook.com/v20.0/local-coexistence-review/messages", "another-token", "text")]
    [InlineData("https://provider.example.invalid/v20.0/local-coexistence-review/messages", "local-disabled", "text")]
    [InlineData("https://graph.facebook.com/v20.0/local-coexistence-review/messages?other=1", "local-disabled", "text")]
    [InlineData("https://graph.facebook.com/v20.0/local-coexistence-review/messages", "local-disabled", "image")]
    public async Task Local_simulation_rejects_other_lines_tokens_hosts_queries_and_media(string url, string token, string type)
    {
        await using var provider = LocalProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("provider");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to = "573000000000", type, text = new { body = "Prueba" } });
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"messaging_product\":{},\"type\":\"text\"}")]
    [InlineData("{\"messaging_product\":\"whatsapp\",\"type\":[]}")]
    [InlineData("invalid-json")]
    public async Task Local_simulation_rejects_invalid_payload_without_throwing(string body)
    {
        await using var provider = LocalProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("provider");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.facebook.com/v20.0/local-coexistence-review/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "local-disabled");
        request.Content = new StringContent(body);
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static IConfiguration Configuration(bool enabled, string database = "senor_arroz_coexistence_local", string host = "127.0.0.1") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppLocalReview:Enabled"] = enabled.ToString(),
            ["ConnectionStrings:DefaultConnection"] = $"Host={host};Database={database}"
        }).Build();

    [Theory]
    [InlineData("Production", "senor_arroz_coexistence_local", "127.0.0.1")]
    [InlineData("Development", "senor_arroz_whatsapp_dev", "127.0.0.1")]
    [InlineData("Development", "senor_arroz_coexistence_local", "database.example.invalid")]
    public void Rejects_production_shared_database_or_remote_database(string name, string database, string host)
    {
        var environment = Mock.Of<IHostEnvironment>(value => value.EnvironmentName == name);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().ConfigureWhatsAppLocalReview(
            Configuration(true, database, host), environment));
    }

    [Fact]
    public void Default_configuration_preserves_background_services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IHostedService>());
        Assert.False(services.ConfigureWhatsAppLocalReview(Configuration(false), Mock.Of<IHostEnvironment>()));
        Assert.Contains(services, value => value.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task Explicit_local_mode_disables_workers_and_prevents_external_http()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("provider");
        services.AddSingleton(Mock.Of<IHostedService>());
        Assert.True(services.ConfigureWhatsAppLocalReview(Configuration(true),
            Mock.Of<IHostEnvironment>(value => value.EnvironmentName == "Development")));
        Assert.DoesNotContain(services, value => value.ServiceType == typeof(IHostedService));
        await using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("provider");
        using var response = await client.GetAsync("https://provider.example.invalid/send");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("local_review_transport_disabled", await response.Content.ReadAsStringAsync());
    }
}
