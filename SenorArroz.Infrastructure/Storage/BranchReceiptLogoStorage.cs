using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Storage;

public sealed class BranchReceiptLogoStorage : IBranchReceiptLogoStorage
{
    private readonly string _webRoot;

    public BranchReceiptLogoStorage(string webRoot)
    {
        _webRoot = webRoot ?? throw new ArgumentNullException(nameof(webRoot));
    }

    public async Task<string> SaveAndReplaceAsync(int branchId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
    {
        var ext = NormalizeExtension(fileExtension);
        var relativeDir = Path.Combine("uploads", "branch-print", branchId.ToString()).Replace('\\', '/');
        var physicalDir = Path.Combine(_webRoot, "uploads", "branch-print", branchId.ToString());
        Directory.CreateDirectory(physicalDir);

        foreach (var existing in Directory.EnumerateFiles(physicalDir, "logo.*"))
            File.Delete(existing);

        var relativeUrl = "/" + relativeDir + "/logo" + ext;
        var physicalPath = Path.Combine(physicalDir, "logo" + ext);
        await File.WriteAllBytesAsync(physicalPath, content, cancellationToken);
        return relativeUrl.Replace('\\', '/');
    }

    public Task ClearAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var physicalDir = Path.Combine(_webRoot, "uploads", "branch-print", branchId.ToString());
        if (!Directory.Exists(physicalDir))
            return Task.CompletedTask;

        foreach (var existing in Directory.EnumerateFiles(physicalDir, "logo.*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(existing);
        }

        return Task.CompletedTask;
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var e = (fileExtension ?? ".png").Trim().ToLowerInvariant();
        if (!e.StartsWith('.'))
            e = "." + e;
        return e is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? e : ".png";
    }
}
