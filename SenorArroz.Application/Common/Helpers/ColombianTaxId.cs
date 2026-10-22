using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Helpers;

public static class ColombianTaxId
{
    public static (string Number, string VerificationDigit) Parse(string identification)
    {
        var parts = identification.Split('-');
        var number = parts[0];
        if (parts.Length > 2 || number.Length is < 1 or > 15 || number.Any(c => c is < '0' or > '9'))
            throw new BusinessException("El NIT debe contener solo números y, opcionalmente, su dígito de verificación separado por guion.");
        int[] weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
        var sum = 0;
        for (var index = 0; index < number.Length; index++) sum += (number[^(index + 1)] - '0') * weights[index];
        var remainder = sum % 11;
        var digit = (remainder > 1 ? 11 - remainder : remainder).ToString();
        if (parts.Length == 2 && parts[1] != digit)
            throw new BusinessException("El dígito de verificación no corresponde al NIT. Verifica la identificación antes de facturar.");
        return (number, digit);
    }
}
