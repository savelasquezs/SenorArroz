namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Subida y borrado de objetos en el bucket de Firebase / Google Cloud Storage.</summary>
public interface IFirebaseGcsStorage
{
    /// <summary>Sube bytes y devuelve URL HTTPS de descarga (formato host Firebase).</summary>
    Task<string> UploadPublicObjectAsync(byte[] content, string objectName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Elimina un objeto por nombre completo en el bucket.</summary>
    Task DeleteObjectAsync(string objectName, CancellationToken cancellationToken = default);

    /// <summary>Elimina objetos cuyo nombre tiene el prefijo dado (p. ej. <c>branch-print/12/</c>).</summary>
    Task DeleteObjectsWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
