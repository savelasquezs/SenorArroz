using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.BusinessDocuments;

internal static class BusinessDocumentFilePolicy
{
    public const int MaxFileSizeBytes = 25 * 1024 * 1024;
    public const int MaxNameLength = 200;
    public const int MaxOriginalFileNameLength = 255;

    public static string ValidateName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new BusinessException("El nombre del documento es requerido.");
        if (normalized.Length > MaxNameLength)
            throw new BusinessException($"El nombre no puede superar {MaxNameLength} caracteres.");
        return normalized;
    }

    public static string ValidateFile(byte[] content, string? originalFileName, string? contentType)
    {
        if (content.Length == 0)
            throw new BusinessException("Selecciona un archivo PDF.");
        if (content.Length > MaxFileSizeBytes)
            throw new BusinessException("El archivo PDF no puede superar 25 MB.");

        var safeFileName = Path.GetFileName(originalFileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            !string.Equals(Path.GetExtension(safeFileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Solo se permiten archivos con extensión .pdf.");
        if (safeFileName.Length > MaxOriginalFileNameLength)
            throw new BusinessException($"El nombre original del archivo no puede superar {MaxOriginalFileNameLength} caracteres.");

        var mediaType = contentType?.Split(';', 2)[0].Trim();
        if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("El tipo de archivo debe ser application/pdf.");

        if (content.Length < 5 ||
            content[0] != (byte)'%' ||
            content[1] != (byte)'P' ||
            content[2] != (byte)'D' ||
            content[3] != (byte)'F' ||
            content[4] != (byte)'-')
            throw new BusinessException("El contenido del archivo no corresponde a un PDF válido.");

        return safeFileName;
    }
}
