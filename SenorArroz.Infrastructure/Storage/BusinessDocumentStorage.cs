using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Storage;

public sealed class BusinessDocumentStorage : IBusinessDocumentStorage
{
    private readonly IFirebaseGcsStorage _gcs;
    private readonly FirebaseStorageOptions _options;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantUsageMeter _usage;

    public BusinessDocumentStorage(
        IFirebaseGcsStorage gcs,
        IOptions<FirebaseStorageOptions> options,
        ICurrentTenant currentTenant,
        ITenantUsageMeter usage)
    {
        _gcs = gcs;
        _options = options.Value;
        _currentTenant = currentTenant;
        _usage = usage;
    }

    public async Task<StoredBusinessDocumentFile> UploadAsync(
        Guid publicId,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var prefix = NormalizedPrefix();
        var objectName = $"{prefix}/{publicId:D}/{Guid.NewGuid():N}.pdf";
        var url = await _gcs.UploadPublicObjectAsync(
            content,
            objectName,
            "application/pdf",
            cancellationToken);
        await _usage.AddStorageBytesAsync(content.LongLength, cancellationToken);
        return new StoredBusinessDocumentFile(url, objectName);
    }

    public Task DeleteObjectAsync(string objectName, CancellationToken cancellationToken = default) =>
        _gcs.DeleteObjectAsync(objectName, cancellationToken);

    public Task DeleteDocumentAsync(Guid publicId, CancellationToken cancellationToken = default) =>
        _gcs.DeleteObjectsWithPrefixAsync($"{NormalizedPrefix()}/{publicId:D}/", cancellationToken);

    private string NormalizedPrefix()
    {
        var prefix = _options.BusinessDocumentsPrefix.Trim().Trim('/');
        var tenantPublicId = _currentTenant.TenantPublicId
            ?? throw new InvalidOperationException("No existe un tenant autenticado para almacenar el documento.");
        return $"tenants/{tenantPublicId:D}/{(string.IsNullOrWhiteSpace(prefix) ? "business-documents" : prefix)}";
    }
}
