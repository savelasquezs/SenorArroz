namespace SenorArroz.Shared.Constants;

/// <summary>
/// Cadenas de rol en JWT/claims y en persistencia (minúsculas).
/// Deben coincidir con los valores del enum <c>UserRole</c> en Domain (p. ej. Superadmin → "superadmin").
/// </summary>
public static class Roles
{
    public const string Superadmin = "superadmin";
    public const string Admin = "admin";
    public const string Cashier = "cashier";
    public const string Kitchen = "kitchen";
    public const string Deliveryman = "deliveryman";

    public static bool EqualsOrdinalIgnoreCase(string? a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool IsSuperadmin(string? role) => EqualsOrdinalIgnoreCase(role, Superadmin);

    public static bool IsAdmin(string? role) => EqualsOrdinalIgnoreCase(role, Admin);

    public static bool IsCashier(string? role) => EqualsOrdinalIgnoreCase(role, Cashier);

    public static bool IsKitchen(string? role) => EqualsOrdinalIgnoreCase(role, Kitchen);

    public static bool IsDeliveryman(string? role) => EqualsOrdinalIgnoreCase(role, Deliveryman);

    public static bool IsAdminOrSuperadmin(string? role) => IsAdmin(role) || IsSuperadmin(role);

    public static bool IsSuperadminOrAdminOrCashier(string? role) =>
        IsSuperadmin(role) || IsAdmin(role) || IsCashier(role);

    public static bool IsAdminOrCashier(string? role) => IsAdmin(role) || IsCashier(role);
}
