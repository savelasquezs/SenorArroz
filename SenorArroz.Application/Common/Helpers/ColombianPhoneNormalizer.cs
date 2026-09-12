using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Helpers;

public static class ColombianPhoneNormalizer
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("57", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.Length == 10 && digits[0] == '3')
        {
            normalized = digits;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static string Normalize(string? value)
    {
        if (TryNormalize(value, out var normalized))
            return normalized;
        throw new BusinessException("El celular debe ser un número móvil colombiano válido de 10 dígitos.");
    }

    // Fixed lines are retained only as administrative contact data. They must never
    // participate in CustomerPhone, OTP, WhatsApp identity, or customer merging.
    public static bool TryNormalizeAdministrativeFixedLine(string? value, out string normalized)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 10 && digits.StartsWith("604", StringComparison.Ordinal))
        {
            normalized = digits;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static bool TryNormalizeCustomerContact(string? value, out string normalized) =>
        TryNormalize(value, out normalized) || TryNormalizeAdministrativeFixedLine(value, out normalized);

    public static string NormalizeCustomerContact(string? value)
    {
        if (TryNormalizeCustomerContact(value, out var normalized))
            return normalized;
        throw new BusinessException("El teléfono debe ser un celular colombiano o una línea fija 604 válida de 10 dígitos.");
    }
}
