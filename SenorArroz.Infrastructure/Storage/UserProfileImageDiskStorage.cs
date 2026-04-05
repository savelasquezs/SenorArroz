using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Storage;

public sealed class UserProfileImageDiskStorage : IUserProfileImageStorage
{
    private readonly string _webRoot;

    public UserProfileImageDiskStorage(string webRoot)
    {
        _webRoot = webRoot ?? throw new ArgumentNullException(nameof(webRoot));
    }

    public async Task<string> SaveAndReplaceAsync(int userId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = NormalizeExtension(fileExtension);
        var uploadsDir = Path.Combine(_webRoot, "uploads", "profile");
        Directory.CreateDirectory(uploadsDir);

        foreach (var old in Directory.GetFiles(uploadsDir, $"{userId}*.*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(old);
        }

        var fileName = $"{userId}_{Guid.NewGuid():N}{ext}";
        var physicalPath = Path.Combine(uploadsDir, fileName);
        await File.WriteAllBytesAsync(physicalPath, content, cancellationToken).ConfigureAwait(false);
        return $"/uploads/profile/{fileName}";
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var e = (fileExtension ?? ".png").Trim().ToLowerInvariant();
        if (!e.StartsWith('.'))
            e = "." + e;
        return e is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? e : ".jpg";
    }
}
