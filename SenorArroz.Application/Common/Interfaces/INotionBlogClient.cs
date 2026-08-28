using SenorArroz.Application.Features.BlogPublishing.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface INotionBlogClient
{
    Task<IReadOnlyList<BlogArticleSummaryDto>> GetApprovedArticlesAsync(CancellationToken cancellationToken = default);
    Task<BlogArticlePreviewDto> GetPreviewAsync(string notionPageId, CancellationToken cancellationToken = default);
    Task MarkPublishedAsync(string notionPageId, string publicUrl, DateTime publishedAtUtc, CancellationToken cancellationToken = default);
}

public interface IBlogPublishingConfiguration
{
    string NotionApiKey { get; }
    string NotionDataSourceId { get; }
    string NotionApiVersion { get; }
    string SiteUrl { get; }
    int TenantId { get; }
}
