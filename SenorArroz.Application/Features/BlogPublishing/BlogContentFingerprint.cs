using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.BlogPublishing;

public static class BlogContentFingerprint
{
    public static string Compute(BlogArticlePreviewDto preview) => Compute(
        preview.Title,
        preview.Slug,
        preview.MetaTitle,
        preview.MetaDescription,
        preview.KeywordPrincipal,
        preview.Intent,
        preview.Blocks);

    public static string Compute(BlogPost post)
    {
        var blocks = JsonSerializer.Deserialize<List<BlogBlockDto>>(post.ContentJson) ?? [];
        return Compute(
            post.Title,
            post.Slug,
            post.MetaTitle,
            post.MetaDescription,
            post.KeywordPrincipal,
            post.Intent,
            blocks);
    }

    private static string Compute(
        string title,
        string slug,
        string metaTitle,
        string metaDescription,
        string? keywordPrincipal,
        string? intent,
        IReadOnlyList<BlogBlockDto> blocks)
    {
        var payload = new
        {
            Title = title.Trim(),
            Slug = slug.Trim(),
            MetaTitle = metaTitle.Trim(),
            MetaDescription = metaDescription.Trim(),
            KeywordPrincipal = keywordPrincipal?.Trim(),
            Intent = intent?.Trim(),
            Blocks = blocks,
        };

        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
