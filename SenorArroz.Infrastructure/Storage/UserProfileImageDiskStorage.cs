using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Storage;

public sealed class UserProfileImageDiskStorage : IUserProfileImageStorage
{
    private readonly string _webRoot;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantUsageMeter _usage;

    public UserProfileImageDiskStorage(string webRoot, ICurrentTenant currentTenant, ITenantUsageMeter usage)
    {
        _webRoot = webRoot ?? throw new ArgumentNullException(nameof(webRoot));
        _currentTenant = currentTenant;
        _usage = usage;
    }

    public async Task<string> SaveAndReplaceAsync(int userId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = NormalizeExtension(fileExtension);
        var tenantPublicId = (_currentTenant.TenantPublicId
            ?? throw new InvalidOperationException("No existe un tenant autenticado para almacenar la imagen."))
            .ToString("D");
        var uploadsDir = Path.Combine(_webRoot, "uploads", "tenants", tenantPublicId, "profile");
        Directory.CreateDirectory(uploadsDir);

        foreach (var old in Directory.GetFiles(uploadsDir, $"{userId}*.*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(old);
        }

        var fileName = $"{userId}_{Guid.NewGuid():N}{ext}";
        var physicalPath = Path.Combine(uploadsDir, fileName);
        await File.WriteAllBytesAsync(physicalPath, content, cancellationToken).ConfigureAwait(false);
        await _usage.AddStorageBytesAsync(content.LongLength, cancellationToken).ConfigureAwait(false);
        return $"/uploads/tenants/{tenantPublicId}/profile/{fileName}";
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var e = (fileExtension ?? ".png").Trim().ToLowerInvariant();
        if (!e.StartsWith('.'))
            e = "." + e;
        return e is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? e : ".jpg";
    }
}
