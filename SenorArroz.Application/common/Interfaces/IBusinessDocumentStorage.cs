namespace SenorArroz.Application.Common.Interfaces;

public sealed record StoredBusinessDocumentFile(string DownloadUrl, string ObjectName);

public interface IBusinessDocumentStorage
{
    Task<StoredBusinessDocumentFile> UploadAsync(
        Guid publicId,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(string objectName, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(Guid publicId, CancellationToken cancellationToken = default);
}
