using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Filtros reutilizables para consultas de listado de <see cref="Order"/> sin includes pesados.
/// </summary>
public static class OrderSearchFilterExtensions
{
    /// <summary>
    /// Exige <paramref name="context"/> para correlacionar búsqueda de cliente vía
    /// <c>EXISTS</c> a <see cref="ApplicationDbContext.Customers"/>, patrón que EF+Npgsql
    /// traduce bien; evita joins implícitos por navegación + <c>ILike</c> en un único
    /// <c>OR</c> (fallaba con InvalidOperationException en traducción SQL).
    /// </summary>
    public static IQueryable<Order> ApplyOrderSearchTermFilter(
        this IQueryable<Order> query,
        ApplicationDbContext context,
        string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return query;

        var term = searchTerm.Trim();
        var pattern = SqlSearchPattern.ILikeContains(term);

        // Cliente: un EXISTS; equivalente a los tres ORs anteriores (nombre, phone1, phone2).
        return query.Where(o =>
            (o.Notes != null && EF.Functions.ILike(o.Notes, pattern))
            || (o.GuestName != null && EF.Functions.ILike(o.GuestName, pattern))
            || (o.CustomerId != null && context.Customers.Any(c => c.Id == o.CustomerId && (
                EF.Functions.ILike(c.Name, pattern)
                || EF.Functions.ILike(c.Phone1, pattern)
                || (c.Phone2 != null && EF.Functions.ILike(c.Phone2, pattern)))))
            // Npgsql: Id.ToString() en LINQ se traduce a cast a texto en la consulta.
            || EF.Functions.ILike(o.Id.ToString(), pattern));
    }

    public static IQueryable<Order> ApplyOrderTotalDigitsPrefix(this IQueryable<Order> query, string? digitsOnly)
    {
        var ranges = OrderTotalPrefixRanges.BuildRanges(digitsOnly ?? string.Empty);
        if (ranges.Count == 0)
            return query;

        if (ranges.Count == 1)
        {
            var (min, max) = ranges[0];
            return query.Where(o => o.Total >= min && o.Total <= max);
        }

        IQueryable<Order>? union = null;
        foreach (var (min, max) in ranges)
        {
            var slice = query.Where(o => o.Total >= min && o.Total <= max);
            union = union == null ? slice : union.Union(slice);
        }

        return union!;
    }

    /// <summary>
    /// <paramref name="appId"/>: al menos un pago por app con ese id.
    /// <paramref name="unsettledOnly"/>: al menos un <c>AppPayment</c> con <c>IsSetted == false</c>;
    /// si <paramref name="appId"/> tiene valor, solo cuentan líneas de esa app.
    /// </summary>
    public static IQueryable<Order> ApplyOrderAppPaymentFilters(
        this IQueryable<Order> query,
        int? appId,
        bool unsettledOnly)
    {
        if (!appId.HasValue && !unsettledOnly)
            return query;

        if (appId.HasValue && !unsettledOnly)
            return query.Where(o => o.AppPayments.Any(ap => ap.AppId == appId.Value));

        if (!appId.HasValue && unsettledOnly)
            return query.Where(o => o.AppPayments.Any(ap => !ap.IsSetted));

        return query.Where(o => o.AppPayments.Any(ap => ap.AppId == appId!.Value && !ap.IsSetted));
    }
}
