using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class LoyaltyCycleStepRepository : ILoyaltyCycleStepRepository
{
    private readonly ApplicationDbContext _context;

    public LoyaltyCycleStepRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetCycleLengthAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var max = await _context.LoyaltyCycleSteps.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.IsActive)
            .Select(s => (int?)s.StepIndex)
            .MaxAsync(cancellationToken);
        return max ?? 0;
    }

    public Task<LoyaltyCycleStep?> GetByBranchAndStepIndexAsync(int branchId, int stepIndex, CancellationToken cancellationToken = default)
    {
        return _context.LoyaltyCycleSteps.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.StepIndex == stepIndex && s.IsActive, cancellationToken);
    }
}
