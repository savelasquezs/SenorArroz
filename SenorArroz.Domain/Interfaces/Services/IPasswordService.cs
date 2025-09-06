// SenorArroz.Domain/Interfaces/Services/IPasswordService.cs
namespace SenorArroz.Domain.Interfaces.Services;

public interface IPasswordService
{
    /// <summary>
    /// Hash de una contraseña usando BCrypt
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifica si una contraseña coincide con el hash
    /// </summary>
    bool VerifyPassword(string password, string hash);
}