using System.Text.Json;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.BlogPublishing.DTOs;

public sealed record BlogRichTextDto(
    string Text,
    string? Href,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    bool Code);

public sealed class BlogBlockDto
{
    public string Type { get; init; } = string.Empty;
    public List<BlogRichTextDto> RichText { get; init; } = [];
    public List<List<BlogRichTextDto>> Cells { get; init; } = [];
    public List<BlogBlockDto> Children { get; init; } = [];
}

public sealed record BlogArticleSummaryDto(
    string NotionPageId,
    string Title,
    string Slug,
    string State,
    bool HumanReviewed,
    string? KeywordPrincipal,
    string? Intent,
    string? MetaTitle,
    string? MetaDescription,
    string? ClientViewUrl,
    DateTime? LastEditedAt);

public sealed record BlogArticlePreviewDto(
    string NotionPageId,
    string Title,
    string Slug,
    string State,
    bool HumanReviewed,
    string? KeywordPrincipal,
    string? Intent,
    string MetaTitle,
    string MetaDescription,
    string ClientViewUrl,
    IReadOnlyList<BlogBlockDto> Blocks,
    IReadOnlyList<string> Warnings,
    DateTime? LastEditedAt);

public sealed record BlogPublishingQueueItemDto(
    string NotionPageId,
    string Title,
    string Slug,
    string State,
    bool HumanReviewed,
    string? KeywordPrincipal,
    string? Intent,
    string? MetaTitle,
    string? MetaDescription,
    string? ClientViewUrl,
    DateTime? LastEditedAt,
    string PublicationStatus,
    bool HasUnpublishedChanges,
    string? PublicUrl,
    DateTime? PublishedAt,
    DateTime? PublishedUpdatedAt,
    string? CheckError);

public sealed record BlogPublishedPostSummaryDto(
    int Id,
    string Title,
    string Slug,
    string MetaDescription,
    string? KeywordPrincipal,
    string? Intent,
    DateTime PublishedAt,
    DateTime UpdatedAt)
{
    public static BlogPublishedPostSummaryDto FromEntity(BlogPost post) => new(
        post.Id,
        post.Title,
        post.Slug,
        post.MetaDescription,
        post.KeywordPrincipal,
        post.Intent,
        post.PublishedAt,
        post.UpdatedAt);
}

public sealed record BlogPublishedPostDto(
    int Id,
    string Title,
    string Slug,
    string MetaTitle,
    string MetaDescription,
    string? KeywordPrincipal,
    string? Intent,
    IReadOnlyList<BlogBlockDto> Blocks,
    DateTime PublishedAt,
    DateTime UpdatedAt)
{
    public static BlogPublishedPostDto FromEntity(BlogPost post)
    {
        var blocks = JsonSerializer.Deserialize<List<BlogBlockDto>>(post.ContentJson) ?? [];
        return new BlogPublishedPostDto(
            post.Id,
            post.Title,
            post.Slug,
            post.MetaTitle,
            post.MetaDescription,
            post.KeywordPrincipal,
            post.Intent,
            blocks,
            post.PublishedAt,
            post.UpdatedAt);
    }
}

public sealed record BlogPublishResultDto(
    BlogPublishedPostDto Post,
    string PublicUrl,
    IReadOnlyList<string> Warnings);
