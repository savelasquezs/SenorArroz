using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class TenantUsageMeter(
    IDbContextFactory<ApplicationDbContext> factory,
    ICurrentTenant currentTenant) : ITenantUsageMeter
{
    public async Task AddStorageBytesAsync(long bytes, CancellationToken cancellationToken = default)
    {
        if (bytes <= 0 || !currentTenant.HasTenant) return;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var month = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO tenant_usage_monthly(tenant_id, month, storage_bytes, updated_at)
                VALUES ({currentTenant.TenantId}, {month}, {bytes}, now())
                ON CONFLICT (tenant_id, month)
                DO UPDATE SET storage_bytes = tenant_usage_monthly.storage_bytes + EXCLUDED.storage_bytes, updated_at = now()", cancellationToken);
            return;
        }

        var usage = await db.TenantUsageMonthly.SingleOrDefaultAsync(x => x.TenantId == currentTenant.TenantId && x.Month == month, cancellationToken);
        if (usage is null)
        {
            usage = new TenantUsageMonthly { TenantId = currentTenant.TenantId, Month = month };
            db.TenantUsageMonthly.Add(usage);
        }
        usage.StorageBytes += bytes;
        usage.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
