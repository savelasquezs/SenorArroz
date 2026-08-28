using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class BlogPost : BaseEntity
{
    public int TenantId { get; set; }
    public string NotionPageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public string? KeywordPrincipal { get; set; }
    public string? Intent { get; set; }
    public string ContentJson { get; set; } = "[]";
    public DateTime PublishedAt { get; set; }
}
