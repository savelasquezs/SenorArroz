using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Storage;

public sealed class UserProfileImageGcsStorage : IUserProfileImageStorage
{
    private readonly IFirebaseGcsStorage _gcs;
    private readonly FirebaseStorageOptions _opt;
    private readonly ICurrentTenant _tenant;

    public UserProfileImageGcsStorage(IFirebaseGcsStorage gcs, ICurrentTenant tenant, IOptions<FirebaseStorageOptions> options)
    {
        _gcs = gcs;
        _tenant = tenant;
        _opt = options.Value;
    }

    public async Task<string> SaveAndReplaceAsync(int userId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = NormalizeExtension(fileExtension);
        var prefix = TenantStoragePath.Combine(_tenant, _opt.ProfilePrefix.Trim().Trim('/'));
        var folderPrefix = $"{prefix}/{userId}/";

        await _gcs.DeleteObjectsWithPrefixAsync(folderPrefix, cancellationToken).ConfigureAwait(false);

        var objectName = $"{prefix}/{userId}/{Guid.NewGuid():N}{ext}";
        var contentType = ContentTypeForExtension(ext);
        return await _gcs.UploadPublicObjectAsync(content, objectName, contentType, cancellationToken).ConfigureAwait(false);
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
