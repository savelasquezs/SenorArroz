using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data;

public sealed class StorefrontMetaAttributionInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        var http = httpContextAccessor.HttpContext;
        if (context is null || http is null) return;

        var userAgent = ReadHeader(http, "X-Storefront-Client-User-Agent", 512);
        var clientIp = ReadHeader(http, "X-Storefront-Client-Ip", 64);
        var fbp = ReadHeader(http, "X-Meta-Fbp", 255);
        var fbc = ReadHeader(http, "X-Meta-Fbc", 255);
        if (userAgent is null && clientIp is null && fbp is null && fbc is null) return;

        foreach (var entry in context.ChangeTracker.Entries<StorefrontCheckout>().Where(x => x.State == EntityState.Added))
        {
            entry.Entity.MetaClientUserAgent ??= userAgent;
            entry.Entity.MetaClientIpAddress ??= clientIp;
            entry.Entity.MetaFbp ??= fbp;
            entry.Entity.MetaFbc ??= fbc;
        }

        foreach (var entry in context.ChangeTracker.Entries<PaymentNotificationOutboxMessage>().Where(x => x.State == EntityState.Added))
        {
            entry.Entity.MetaClientUserAgent ??= userAgent;
            entry.Entity.MetaClientIpAddress ??= clientIp;
            entry.Entity.MetaFbp ??= fbp;
            entry.Entity.MetaFbc ??= fbc;
        }
    }

    private static string? ReadHeader(HttpContext context, string name, int maxLength)
    {
        var value = context.Request.Headers[name].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
