using System.Net;
using System.Net.Http.Headers;
using SenorArroz.API.Services;

namespace SenorArroz.Tests;

public sealed class WhatsAppFlowImageCacheTests
{
    [Fact]
    public async Task SameImageAcrossHttpClientInstances_ReusesCachedPayload()
    {
        using var cache = new WhatsAppFlowImageCache();
        using var handler = new ImageHandler(new byte[4_096]);
        using var http = new HttpClient(handler);
        var first = new WhatsAppFlowImageService(http, cache);
        var second = new WhatsAppFlowImageService(http, cache);
        var url = ImageUrl("shared");

        var initial = await first.GetBase64Async(url, CancellationToken.None);
        var repeated = await second.GetBase64Async(url, CancellationToken.None);

        Assert.NotNull(initial);
        Assert.Equal(initial, repeated);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DistinctProductImages_CannotGrowBase64CacheWithoutBound()
    {
        // 60 x 90 KiB images would retain ~14 MiB of UTF-16 base64 strings in an
        // unbounded cache. The dedicated cache has an 8 MiB payload budget.
        using var cache = new WhatsAppFlowImageCache();
        using var handler = new ImageHandler(new byte[90 * 1024]);
        using var http = new HttpClient(handler);
        var service = new WhatsAppFlowImageService(http, cache);

        for (var i = 0; i < 60; i++)
            Assert.NotNull(await service.GetBase64Async(ImageUrl(i.ToString()), CancellationToken.None));

        Assert.Equal(60, handler.Calls);
        Assert.InRange(cache.Entries.Count, 1, 40);
    }

    [Theory]
    [InlineData("https://example.com/x.png")]
    [InlineData("http://firebasestorage.googleapis.com/x.png")]
    [InlineData("https://firebasestorage.googleapis.com:8443/x.png")]
    public async Task UnsafeImageOrigin_IsNeverFetched(string url)
    {
        using var cache = new WhatsAppFlowImageCache();
        using var handler = new ImageHandler(new byte[1_024]);
        using var http = new HttpClient(handler);
        var service = new WhatsAppFlowImageService(http, cache);

        Assert.Null(await service.GetBase64Async(url, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, cache.Entries.Count);
    }

    [Fact]
    public async Task OversizedImage_IsNotCachedOrReturned()
    {
        using var cache = new WhatsAppFlowImageCache();
        using var handler = new ImageHandler(new byte[100 * 1024 + 1]);
        using var http = new HttpClient(handler);
        var service = new WhatsAppFlowImageService(http, cache);
        var url = ImageUrl("oversized");

        Assert.Null(await service.GetBase64Async(url, CancellationToken.None));
        Assert.Null(await service.GetBase64Async(url, CancellationToken.None));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(0, cache.Entries.Count);
    }

    [Fact]
    public async Task TemporaryNetworkFailure_PreservesOneMinuteNegativeCache()
    {
        using var cache = new WhatsAppFlowImageCache();
        using var handler = new ImageHandler(null);
        using var http = new HttpClient(handler);
        var service = new WhatsAppFlowImageService(http, cache);
        var url = ImageUrl("unreachable");

        Assert.Null(await service.GetBase64Async(url, CancellationToken.None));
        Assert.Null(await service.GetBase64Async(url, CancellationToken.None));

        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, cache.Entries.Count);
    }

    private static string ImageUrl(string id) =>
        $"https://firebasestorage.googleapis.com/v0/b/test/o/{id}.png?alt=media";

    private sealed class ImageHandler(byte[]? content) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (content is null) throw new HttpRequestException("Simulated network failure");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }
}
