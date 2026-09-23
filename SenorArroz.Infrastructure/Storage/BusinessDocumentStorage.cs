using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Storage;

public sealed class BusinessDocumentStorage : IBusinessDocumentStorage
{
    private readonly IFirebaseGcsStorage _gcs;
    private readonly FirebaseStorageOptions _options;
    private readonly ICurrentTenant _tenant;

    public BusinessDocumentStorage(
        IFirebaseGcsStorage gcs,
        ICurrentTenant tenant,
        IOptions<FirebaseStorageOptions> options)
    {
        _gcs = gcs;
        _tenant = tenant;
        _options = options.Value;
    }

    public async Task<StoredBusinessDocumentFile> UploadAsync(
        Guid publicId,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var prefix = TenantStoragePath.Combine(_tenant, NormalizedPrefix());
        var objectName = $"{prefix}/{publicId:D}/{Guid.NewGuid():N}.pdf";
        var url = await _gcs.UploadPublicObjectAsync(
            content,
            objectName,
            "application/pdf",
            cancellationToken);
        return new StoredBusinessDocumentFile(url, objectName);
    }

    public Task DeleteObjectAsync(string objectName, CancellationToken cancellationToken = default) =>
        _gcs.DeleteObjectAsync(objectName, cancellationToken);

    public Task DeleteDocumentAsync(Guid publicId, CancellationToken cancellationToken = default) =>
        _gcs.DeleteObjectsWithPrefixAsync($"{TenantStoragePath.Combine(_tenant, NormalizedPrefix())}/{publicId:D}/", cancellationToken);

    private string NormalizedPrefix()
    {
        var prefix = _options.BusinessDocumentsPrefix.Trim().Trim('/');
        return string.IsNullOrWhiteSpace(prefix) ? "business-documents" : prefix;
    }
}
