using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.EmployeeLeaves;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Storage;

/// <summary>Almacenamiento privado: no emite URLs ni tokens de descarga Firebase.</summary>
public sealed class EmployeeLeaveStorage(IConfiguration configuration, ICurrentTenant tenant) : IEmployeeLeaveStorage
{
    private StorageClient? client;
    private StorageClient Client => client ??= StorageClient.Create();
    private string Bucket => configuration["EmployeeLeaves:PrivateBucket"]?.Trim()
        is { Length: > 0 } name ? name : throw new BusinessException("Los soportes privados aún no están configurados.");
    private string Prefix => tenant.HasTenant ? $"tenants/{tenant.TenantId}/employee-leaves/"
        : throw new UnauthorizedAccessException();

    private async Task EnsurePrivateAsync(CancellationToken ct)
    {
        var bucket = await Client.GetBucketAsync(Bucket, cancellationToken: ct);
        if (bucket.IamConfiguration?.PublicAccessPrevention != "enforced")
            throw new BusinessException("El almacenamiento de soportes debe tener prevención de acceso público obligatoria.");
    }

    public async Task<string> UploadAsync(Guid publicId, Guid uploadKey, byte[] content, string contentType, CancellationToken ct)
    {
        if (content.Length == 0 || content.Length > EmployeeLeaveFiles.MaxBytes)
            throw new BusinessException("El archivo supera el límite permitido.");
        await EnsurePrivateAsync(ct);
        // Clave física nueva por intento: limpiar un fallo nunca elimina el archivo de otro intento confirmado.
        var key = $"{Prefix}{publicId:D}/{uploadKey:D}/{Guid.NewGuid():N}";
        using var stream = new MemoryStream(content, writable: false);
        await Client.UploadObjectAsync(Bucket, key, contentType, stream,
            new UploadObjectOptions { IfGenerationMatch = 0 }, ct);
        return key;
    }

    public async Task<byte[]> DownloadAsync(string storageKey, int expectedSize, CancellationToken ct)
    {
        ValidateKey(storageKey);
        await EnsurePrivateAsync(ct);
        if (expectedSize <= 0 || expectedSize > EmployeeLeaveFiles.MaxBytes)
            throw new BusinessException("Tamaño de soporte no válido.");
        var metadata = await Client.GetObjectAsync(Bucket, storageKey, cancellationToken: ct);
        if (metadata.Size != (ulong)expectedSize || metadata.Metadata?.ContainsKey("firebaseStorageDownloadTokens") == true)
            throw new BusinessException("El soporte no coincide con el archivo privado registrado.");
        using var stream = new MemoryStream(expectedSize);
        await Client.DownloadObjectAsync(Bucket, storageKey, stream,
            new DownloadObjectOptions { IfGenerationMatch = metadata.Generation }, ct);
        if (stream.Length != expectedSize) throw new BusinessException("El archivo recibido está incompleto.");
        return stream.ToArray();
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        ValidateKey(storageKey);
        try { await Client.DeleteObjectAsync(Bucket, storageKey, cancellationToken: ct); }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) { }
    }

    private void ValidateKey(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal) || key.Contains("..", StringComparison.Ordinal))
            throw new UnauthorizedAccessException();
    }
}
