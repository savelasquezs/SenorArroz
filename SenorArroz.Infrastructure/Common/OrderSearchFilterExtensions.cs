using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Filtros reutilizables para consultas de listado de <see cref="Order"/> sin includes pesados.
/// </summary>
public static class OrderSearchFilterExtensions
{
    public static IQueryable<Order> ApplyOrderSearchTermFilter(this IQueryable<Order> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return query;

        var term = searchTerm.Trim();
        var pattern = SqlSearchPattern.ILikeContains(term);

        return query.Where(o =>
            (o.Notes != null && EF.Functions.ILike(o.Notes, pattern))
            || (o.GuestName != null && EF.Functions.ILike(o.GuestName, pattern))
            || (o.Customer != null && EF.Functions.ILike(o.Customer.Name, pattern))
            || (o.Customer != null && EF.Functions.ILike(o.Customer.Phone1, pattern))
            || (o.Customer != null && o.Customer.Phone2 != null && EF.Functions.ILike(o.Customer.Phone2, pattern))
            || EF.Functions.ILike(string.Concat(string.Empty, o.Id), pattern));
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
