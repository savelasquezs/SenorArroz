namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Opciones de “vuelto” para redondear el efectivo que lleva el domiciliario (múltiplos de 50k / 100k).
/// </summary>
public static class DeliveryAdvanceVueltoHelper
{
    public readonly record struct VueltoOption(decimal VueltoAdd, decimal TargetCarry);

    /// <summary>
    /// Reglas: múltiplo de 100k → solo sin vuelto; múltiplo de 50k (y no 100k) → solo subir al siguiente 100k;
    /// resto → subir a siguiente 50k si (T mod 100k)/10k &lt; 5, y siempre opción al siguiente 100k.
    /// </summary>
    public static IReadOnlyList<VueltoOption> GetOptionsForTotal(int totalCop)
    {
        if (totalCop <= 0)
            return Array.Empty<VueltoOption>();

        long T = totalCop;

        if (T % 100_000 == 0)
            return new[] { new VueltoOption(0m, T) };

        if (T % 50_000 == 0)
        {
            long h100 = (long)Math.Ceiling(T / 100_000.0) * 100_000;
            return new[] { new VueltoOption(h100 - T, h100) };
        }

        int q = (int)((T % 100_000) / 10_000);
        var list = new List<VueltoOption>();

        if (q < 5)
        {
            long h50 = (long)Math.Ceiling(T / 50_000.0) * 50_000;
            if (h50 > T)
                list.Add(new VueltoOption(h50 - T, h50));
        }

        long h100b = (long)Math.Ceiling(T / 100_000.0) * 100_000;
        if (h100b > T)
            list.Add(new VueltoOption(h100b - T, h100b));

        return list
            .GroupBy(x => x.TargetCarry)
            .Select(g => g.First())
            .OrderBy(x => x.TargetCarry)
            .ToList();
    }

    public static bool IsValidVueltoAdd(int totalCop, decimal vueltoAdd)
    {
        return GetOptionsForTotal(totalCop).Any(o => o.VueltoAdd == vueltoAdd);
    }
}
