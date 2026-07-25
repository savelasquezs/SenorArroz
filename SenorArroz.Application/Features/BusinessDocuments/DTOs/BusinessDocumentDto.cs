using Microsoft.Extensions.Options;
using SenorArroz.Application.Common;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.BusinessDocuments.DTOs;

public sealed class BusinessDocumentDto
{
    public int Id { get; init; }
    public Guid PublicId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string PublicDownloadUrl { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

internal sealed class BusinessDocumentDtoFactory
{
    private readonly string? _publicApiBaseUrl;

    public BusinessDocumentDtoFactory(IOptions<ApiPublicOptions> options)
    {
        _publicApiBaseUrl = options.Value.BaseUrl;
    }

    public BusinessDocumentDto Create(BusinessDocument document)
    {
        var path = $"/api/public/business-documents/{document.PublicId:D}/download";
        return new BusinessDocumentDto
        {
            Id = document.Id,
            PublicId = document.PublicId,
            Name = document.Name,
            DownloadUrl = document.DownloadUrl,
            PublicDownloadUrl = PublicUrlHelper.ToAbsolutePublicUrl(_publicApiBaseUrl, path) ?? path,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSizeBytes = document.FileSizeBytes,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
        };
    }
}
