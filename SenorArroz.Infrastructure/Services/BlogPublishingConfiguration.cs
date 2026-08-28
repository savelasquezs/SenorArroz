using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public sealed class BlogPublishingConfiguration : IBlogPublishingConfiguration
{
    public BlogPublishingConfiguration(IConfiguration configuration)
    {
        NotionApiKey = FirstNonEmpty(
            configuration["NOTION_API_KEY"],
            configuration["BlogPublishing:NotionApiKey"]) ?? string.Empty;
        NotionDataSourceId = FirstNonEmpty(
            configuration["NOTION_BLOG_DATA_SOURCE_ID"],
            configuration["BlogPublishing:NotionDataSourceId"]) ?? string.Empty;
        NotionApiVersion = FirstNonEmpty(
            configuration["NOTION_API_VERSION"],
            configuration["BlogPublishing:NotionApiVersion"]) ?? "2026-03-11";
        SiteUrl = (FirstNonEmpty(
            configuration["BLOG_SITE_URL"],
            configuration["BlogPublishing:SiteUrl"]) ?? "https://senorarroz.com").TrimEnd('/');

        var tenantValue = FirstNonEmpty(
            configuration["BLOG_TENANT_ID"],
            configuration["BlogPublishing:TenantId"]);
        TenantId = int.TryParse(tenantValue, out var tenantId) && tenantId > 0 ? tenantId : 1;
    }

    public string NotionApiKey { get; }
    public string NotionDataSourceId { get; }
    public string NotionApiVersion { get; }
    public string SiteUrl { get; }
    public int TenantId { get; }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
}
