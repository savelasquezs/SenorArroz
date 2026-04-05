namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Guarda la foto de perfil en disco o en Storage; devuelve ruta relativa a la API o URL absoluta.</summary>
public interface IUserProfileImageStorage
{
    Task<string> SaveAndReplaceAsync(int userId, byte[] content, string fileExtension, CancellationToken cancellationToken = default);
}
