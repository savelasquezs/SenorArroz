using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Common.Services;

public static class EmployeePasswordConfirmation
{
    // Confirmar una operación no crea tokens, no cambia ActiveSessionId y no cierra Flutter.
    public static void Verify(User employee, string? password, IPasswordService passwords)
    {
        if (!employee.Active || employee.IsTerminal)
            throw new UnauthorizedAccessException("La operación requiere una cuenta personal activa.");
        if (string.IsNullOrEmpty(password) || password.Length > 256
            || !passwords.VerifyPassword(password, employee.PasswordHash))
            throw new BusinessException("Contraseña incorrecta.");
    }
}
