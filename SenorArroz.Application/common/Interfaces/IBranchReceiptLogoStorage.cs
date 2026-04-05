namespace SenorArroz.Application.Common.Interfaces;

public interface IBranchReceiptLogoStorage
{
    /// <summary>Guarda el archivo y devuelve la ruta bajo la API (ej. /uploads/...) o URL absoluta de Storage.</summary>
    Task<string> SaveAndReplaceAsync(int branchId, byte[] content, string fileExtension, CancellationToken cancellationToken = default);

    /// <summary>Quita el logo (disco o objetos bajo el prefijo de la sucursal en Storage).</summary>
    Task ClearAsync(int branchId, CancellationToken cancellationToken = default);
}
