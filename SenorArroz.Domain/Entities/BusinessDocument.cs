using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class BusinessDocument : BaseEntity
{
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string StorageObjectName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long FileSizeBytes { get; set; }
}
