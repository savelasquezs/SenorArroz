// SenorArroz.Infrastructure/Services/PasswordService.cs
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Infrastructure.Services;

public class PasswordService : IPasswordService
{
    public string HashPassword(string password)
    {
        // BCrypt es el estándar para hashear passwords
        // El método GenerateHashFromPassword incluye salt automáticamente
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            // En caso de hash corrupto o formato inválido
            return false;
        }
    }
}