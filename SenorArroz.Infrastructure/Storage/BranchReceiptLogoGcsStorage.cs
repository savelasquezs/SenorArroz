using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Storage;

/// <summary>Logo de ticket en Firebase/GCS; persiste URL absoluta en <c>receipt_logo_path</c>.</summary>
public sealed class BranchReceiptLogoGcsStorage : IBranchReceiptLogoStorage
{
    private readonly IFirebaseGcsStorage _gcs;
    private readonly FirebaseStorageOptions _opt;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantUsageMeter _usage;

    public BranchReceiptLogoGcsStorage(
        IFirebaseGcsStorage gcs,
        IOptions<FirebaseStorageOptions> options,
        ICurrentTenant currentTenant,
        ITenantUsageMeter usage)
    {
        _gcs = gcs;
        _opt = options.Value;
        _currentTenant = currentTenant;
        _usage = usage;
    }

    public async Task<string> SaveAndReplaceAsync(int branchId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
    {
        var ext = NormalizeExtension(fileExtension);
        var prefix = TenantPrefix();
        var folderPrefix = $"{prefix}/{branchId}/";

        await _gcs.DeleteObjectsWithPrefixAsync(folderPrefix, cancellationToken).ConfigureAwait(false);

        var objectName = $"{prefix}/{branchId}/logo{ext}";
        var contentType = ContentTypeForExtension(ext);
        var url = await _gcs.UploadPublicObjectAsync(content, objectName, contentType, cancellationToken).ConfigureAwait(false);
        await _usage.AddStorageBytesAsync(content.LongLength, cancellationToken).ConfigureAwait(false);
        return url;
    }

    public Task ClearAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var prefix = TenantPrefix();
        var folderPrefix = $"{prefix}/{branchId}/";
        return _gcs.DeleteObjectsWithPrefixAsync(folderPrefix, cancellationToken);
    }

    private static string ContentTypeForExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png",
        };

    private static string NormalizeExtension(string fileExtension)
    {
        var e = (fileExtension ?? ".png").Trim().ToLowerInvariant();
        if (!e.StartsWith('.'))
            e = "." + e;
        return e is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? e : ".png";
    }

    private string TenantPrefix()
    {
        var tenantPublicId = _currentTenant.TenantPublicId
            ?? throw new InvalidOperationException("No existe un tenant autenticado para almacenar el logo.");
        var configured = _opt.BranchPrintPrefix.Trim().Trim('/');
        var prefix = string.IsNullOrWhiteSpace(configured) ? "branch-print" : configured;
        return $"tenants/{tenantPublicId:D}/{prefix}";
    }
}
