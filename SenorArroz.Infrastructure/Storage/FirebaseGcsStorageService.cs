using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Storage;

public sealed class FirebaseGcsStorageService : IFirebaseGcsStorage
{
    private readonly FirebaseStorageOptions _opt;
    private StorageClient? _client;

    public FirebaseGcsStorageService(IOptions<FirebaseStorageOptions> options)
    {
        _opt = options.Value;
    }

    private StorageClient Client => _client ??= StorageClient.Create();

    public async Task<string> UploadPublicObjectAsync(byte[] content, string objectName, string contentType, CancellationToken cancellationToken = default)
    {
        EnsureReady();

        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("objectName requerido.", nameof(objectName));

        var name = objectName.Trim().TrimStart('/');
        var ct = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        var bucket = _opt.Bucket.Trim();

        UploadObjectOptions? uploadOptions = null;
        if (_opt.UploadWithPublicReadAcl)
            uploadOptions = new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead };

        using var stream = new MemoryStream(content, writable: false);
        if (uploadOptions is not null)
            await Client.UploadObjectAsync(bucket, name, ct, stream, uploadOptions, cancellationToken).ConfigureAwait(false);
        else
            await Client.UploadObjectAsync(bucket, name, ct, stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return BuildPublicUrl(bucket, name);
    }

    public async Task DeleteObjectAsync(string objectName, CancellationToken cancellationToken = default)
    {
        EnsureReady();

        if (string.IsNullOrWhiteSpace(objectName))
            return;

        var name = objectName.Trim().TrimStart('/');
        var bucket = _opt.Bucket.Trim();
        try
        {
            await Client.DeleteObjectAsync(bucket, name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // idempotente
        }
    }

    public async Task DeleteObjectsWithPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        EnsureReady();

        if (string.IsNullOrWhiteSpace(prefix))
            return;

        var p = prefix.Trim().TrimStart('/');
        var bucket = _opt.Bucket.Trim();
        var names = Client.ListObjects(bucket, p).Select(o => o.Name).ToList();
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteObjectAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureReady()
    {
        if (!_opt.Enabled)
            throw new InvalidOperationException("Firebase Storage está desactivado (FirebaseStorage:Enabled).");
        if (string.IsNullOrWhiteSpace(_opt.Bucket))
            throw new InvalidOperationException("Firebase Storage: falta el nombre del bucket (FirebaseStorage:Bucket).");
    }

    /// <summary>URL pública típica de descarga (requiere objeto o bucket legible públicamente).</summary>
    internal static string BuildPublicUrl(string bucket, string objectName)
    {
        var b = Uri.EscapeDataString(bucket);
        var o = Uri.EscapeDataString(objectName);
        return $"https://firebasestorage.googleapis.com/v0/b/{b}/o/{o}?alt=media";
    }
}
