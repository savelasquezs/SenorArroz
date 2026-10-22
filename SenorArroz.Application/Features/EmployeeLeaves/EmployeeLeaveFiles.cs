using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public static class EmployeeLeaveFiles
{
    public const int MaxFiles = 5;
    public const int MaxBytes = 5 * 1024 * 1024;

    public static (string FileName, string ContentType) Validate(string name, string mime, byte[] content)
    {
        if (content.Length == 0 || content.Length > MaxBytes)
            throw new BusinessException("El soporte debe pesar entre 1 byte y 5 MB.");
        var type = content.AsSpan().StartsWith("%PDF-"u8) ? "application/pdf"
            : content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
            : content.AsSpan().StartsWith(new byte[] { 255, 216, 255 }) ? "image/jpeg" : null;
        if (type is null || !string.Equals(type, mime, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Adjunta un PDF, PNG o JPEG cuyo contenido coincida con el tipo de archivo.");
        var safeName = Path.GetFileName(name.Replace('\\', '/')).Trim();
        if (safeName.Length == 0 || safeName.Length > 150 || safeName.Any(char.IsControl))
            throw new BusinessException("Nombre de archivo no válido.");
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (!(type == "application/pdf" && extension == ".pdf" || type == "image/png" && extension == ".png"
            || type == "image/jpeg" && extension is ".jpg" or ".jpeg"))
            throw new BusinessException("La extensión del soporte no coincide con su contenido.");
        return (safeName, type);
    }
}
