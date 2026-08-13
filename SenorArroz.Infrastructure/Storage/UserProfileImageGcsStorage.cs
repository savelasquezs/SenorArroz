using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Storage;

public sealed class UserProfileImageGcsStorage : IUserProfileImageStorage
{
    private readonly IFirebaseGcsStorage _gcs;
    private readonly FirebaseStorageOptions _opt;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantUsageMeter _usage;

    public UserProfileImageGcsStorage(
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

    public async Task<string> SaveAndReplaceAsync(int userId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = NormalizeExtension(fileExtension);
        var tenantPublicId = _currentTenant.TenantPublicId
            ?? throw new InvalidOperationException("No existe un tenant autenticado para almacenar la imagen.");
        var configured = _opt.ProfilePrefix.Trim().Trim('/');
        var prefix = $"tenants/{tenantPublicId:D}/{(string.IsNullOrWhiteSpace(configured) ? "profile" : configured)}";
        var folderPrefix = $"{prefix}/{userId}/";

        await _gcs.DeleteObjectsWithPrefixAsync(folderPrefix, cancellationToken).ConfigureAwait(false);

        var objectName = $"{prefix}/{userId}/{Guid.NewGuid():N}{ext}";
        var contentType = ContentTypeForExtension(ext);
        var url = await _gcs.UploadPublicObjectAsync(content, objectName, contentType, cancellationToken).ConfigureAwait(false);
        await _usage.AddStorageBytesAsync(content.LongLength, cancellationToken).ConfigureAwait(false);
        return url;
    }

    private static string ContentTypeForExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg",
        };

    private static string NormalizeExtension(string fileExtension)
    {
        var e = (fileExtension ?? ".jpg").Trim().ToLowerInvariant();
        if (!e.StartsWith('.'))
            e = "." + e;
        return e is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? e : ".jpg";
    }
}
