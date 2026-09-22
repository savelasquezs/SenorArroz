using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Services;

public static class TenantWorkerRunner
{
    public static async Task<TResult> RunInSystemScopeAsync<TResult>(
        IServiceScopeFactory scopeFactory,
        Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var executionContext = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
        using var systemScope = executionContext.BeginSystemScope();
        return await action(scope.ServiceProvider);
    }

    public static async Task RunForEachActiveTenantAsync(
        IServiceScopeFactory scopeFactory,
        Func<IServiceProvider, int, Task> action,
        CancellationToken cancellationToken)
    {
        var tenantIds = await RunInSystemScopeAsync(scopeFactory, async services =>
        {
            var db = services.GetRequiredService<IApplicationDbContext>();
            return await db.Tenants.AsNoTracking()
                .Where(x => x.IsActive && x.Status == TenantStatus.Active)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        });

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var executionContext = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
            using var tenantScope = executionContext.BeginTenantScope(tenantId);
            await action(scope.ServiceProvider, tenantId);
        }
    }
}
