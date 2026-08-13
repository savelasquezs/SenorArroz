using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Services;

public sealed class TenantHubGroupResolver(IApplicationDbContext db)
{
    public async Task<int> TenantIdAsync(int branchId, CancellationToken cancellationToken = default) =>
        await db.Branches.AsNoTracking().Where(x => x.Id == branchId).Select(x => x.TenantId!.Value).SingleAsync(cancellationToken);
}
