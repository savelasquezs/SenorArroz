namespace SenorArroz.Application.Common.Interfaces;

public interface IBranchReceiptLogoStorage
{
    /// <summary>Guarda el archivo y devuelve la ruta web relativa (ej. /uploads/branch-print/1/logo.png).</summary>
    Task<string> SaveAndReplaceAsync(int branchId, byte[] content, string fileExtension, CancellationToken cancellationToken = default);

    /// <summary>Elimina archivos logo.* de la carpeta de la sucursal.</summary>
    Task ClearAsync(int branchId, CancellationToken cancellationToken = default);
}
