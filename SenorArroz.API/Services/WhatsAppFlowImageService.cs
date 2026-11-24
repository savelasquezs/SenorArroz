using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace SenorArroz.API.Services;

// Keep base64 product images separate from the shared application cache. All entries
// use UTF-16 payload bytes as the same size unit, with a fixed 8 MiB budget.
public sealed class WhatsAppFlowImageCache : IDisposable
{
    public const long MaxCachedBytes = 8L * 1024 * 1024;
    public MemoryCache Entries { get; } = new(new MemoryCacheOptions { SizeLimit = MaxCachedBytes });
    public void Dispose() => Entries.Dispose();
}

public sealed class WhatsAppFlowImageService(HttpClient http, WhatsAppFlowImageCache cache)
{
    public async Task<string?> GetBase64Async(string? url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "firebasestorage.googleapis.com" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        var key = "flow-image:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url!)));
        if (cache.Entries.TryGetValue(key, out string? cached)) return cached;
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 100 * 1024
                || response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png"))
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = new byte[100 * 1024 + 1];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = await stream.ReadAsync(bytes.AsMemory(read), ct);
                if (count == 0) break;
                read += count;
            }
            if (read == 0 || read > 100 * 1024) return null;
            var result = Convert.ToBase64String(bytes, 0, read);
            cache.Entries.Set(key, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                Size = result.Length * sizeof(char)
            });
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            cache.Entries.Set<string?>(key, null, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                Size = 1
            });
            return null;
        }
    }
}
