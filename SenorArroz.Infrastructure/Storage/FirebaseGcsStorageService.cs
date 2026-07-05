using Google;
using Google.Apis.Storage.v1.Data;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

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

        var downloadToken = Guid.NewGuid().ToString("D");
        var destination = new StorageObject
        {
            Bucket = bucket,
            Name = name,
            ContentType = ct,
            Metadata = new Dictionary<string, string>
            {
                ["firebaseStorageDownloadTokens"] = downloadToken
            }
        };

        using var stream = new MemoryStream(content, writable: false);
        await Client.UploadObjectAsync(destination, stream, uploadOptions, cancellationToken).ConfigureAwait(false);

        return BuildFirebaseDownloadUrl(bucket, name, downloadToken);
    }

    public async Task<string> EnsureDownloadUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        EnsureReady();

        if (string.IsNullOrWhiteSpace(url) || url.Contains("token=", StringComparison.OrdinalIgnoreCase))
            return url;

        if (!TryParseFirebaseStorageUrl(url, out var bucket, out var objectName))
            return url;

        var storageObject = await Client.GetObjectAsync(bucket, objectName, cancellationToken: cancellationToken).ConfigureAwait(false);
        storageObject.Metadata ??= new Dictionary<string, string>();

        if (!storageObject.Metadata.TryGetValue("firebaseStorageDownloadTokens", out var tokens) || string.IsNullOrWhiteSpace(tokens))
        {
            tokens = Guid.NewGuid().ToString("D");
            storageObject.Metadata["firebaseStorageDownloadTokens"] = tokens;
            storageObject = await Client.UpdateObjectAsync(storageObject, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var token = tokens
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(token)
            ? url
            : BuildFirebaseDownloadUrl(storageObject.Bucket ?? bucket, storageObject.Name ?? objectName, token);
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

    internal static string BuildFirebaseDownloadUrl(string bucket, string objectName, string token)
    {
        var url = BuildPublicUrl(bucket, objectName);
        return $"{url}&token={Uri.EscapeDataString(token)}";
    }

    private static bool TryParseFirebaseStorageUrl(string url, out string bucket, out string objectName)
    {
        bucket = string.Empty;
        objectName = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (string.Equals(uri.Host, "firebasestorage.googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.Segments
                .Select(x => x.Trim('/'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            var bucketIndex = Array.FindIndex(segments, x => string.Equals(x, "b", StringComparison.OrdinalIgnoreCase));
            var objectIndex = Array.FindIndex(segments, x => string.Equals(x, "o", StringComparison.OrdinalIgnoreCase));
            if (bucketIndex < 0 || objectIndex < 0 || bucketIndex + 1 >= segments.Length || objectIndex + 1 >= segments.Length)
                return false;

            bucket = Uri.UnescapeDataString(segments[bucketIndex + 1]);
            objectName = Uri.UnescapeDataString(segments[objectIndex + 1]);
            return !string.IsNullOrWhiteSpace(bucket) && !string.IsNullOrWhiteSpace(objectName);
        }

        if (string.Equals(uri.Host, "storage.googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.AbsolutePath.Trim('/');
            var slash = path.IndexOf('/');
            if (slash <= 0 || slash >= path.Length - 1)
                return false;

            bucket = Uri.UnescapeDataString(path[..slash]);
            objectName = Uri.UnescapeDataString(path[(slash + 1)..]);
            return !string.IsNullOrWhiteSpace(bucket) && !string.IsNullOrWhiteSpace(objectName);
        }

        return false;
    }
}
