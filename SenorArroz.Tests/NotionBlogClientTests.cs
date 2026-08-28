using System.Net;
using System.Text;
using System.Text.Json;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class NotionBlogClientTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("select")]
    public async Task GetApprovedArticlesAsync_UsesConfiguredStateTypeAndOmitsInitialCursor(string stateType)
    {
        var handler = new NotionHandler(stateType);
        var client = new NotionBlogClient(new HttpClient(handler), new BlogConfiguration());

        var articles = await client.GetApprovedArticlesAsync();

        var article = Assert.Single(articles);
        Assert.Equal("Artículo aprobado", article.Title);
        using var body = JsonDocument.Parse(handler.QueryBody!);
        var stateFilter = body.RootElement.GetProperty("filter").GetProperty("and")[0];
        Assert.Equal("Aprobado", stateFilter.GetProperty(stateType).GetProperty("equals").GetString());
        Assert.False(body.RootElement.TryGetProperty("start_cursor", out _));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("select")]
    public async Task MarkPublishedAsync_UsesConfiguredStateType(string stateType)
    {
        var handler = new NotionHandler(stateType);
        var client = new NotionBlogClient(new HttpClient(handler), new BlogConfiguration());

        await client.MarkPublishedAsync("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "https://srarroz.com/blog/prueba", DateTime.UtcNow);

        using var body = JsonDocument.Parse(handler.PatchBody!);
        var state = body.RootElement.GetProperty("properties").GetProperty("Estado");
        Assert.Equal("Publicado", state.GetProperty(stateType).GetProperty("name").GetString());
    }

    private sealed class BlogConfiguration : IBlogPublishingConfiguration
    {
        public string NotionApiKey => "secret_test";
        public string NotionDataSourceId => "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        public string NotionApiVersion => "2026-03-11";
        public string SiteUrl => "https://srarroz.com";
        public int TenantId => 1;
    }

    private sealed class NotionHandler(string stateType) : HttpMessageHandler
    {
        public string? QueryBody { get; private set; }
        public string? PatchBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.StartsWith("/v1/data_sources/"))
                return Json(JsonSerializer.Serialize(new
                {
                    properties = new Dictionary<string, object>
                    {
                        ["Estado"] = new { type = stateType },
                    },
                }));

            if (request.Method == HttpMethod.Post)
            {
                QueryBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json($$$"""
                    {
                      "results": [{
                        "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        "last_edited_time": "2026-08-28T12:00:00Z",
                        "properties": {
                          "Tema": { "title": [{ "plain_text": "Artículo aprobado" }] },
                          "Slug": { "rich_text": [{ "plain_text": "articulo-aprobado" }] },
                          "Estado": { "{{{stateType}}}": { "name": "Aprobado" } },
                          "Revisión humana": { "checkbox": true }
                        }
                      }],
                      "has_more": false,
                      "next_cursor": null
                    }
                    """);
            }

            PatchBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json("{}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
