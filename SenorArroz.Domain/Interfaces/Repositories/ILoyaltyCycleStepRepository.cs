using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface ILoyaltyCycleStepRepository
{
    /// <summary>Mayor <see cref="LoyaltyCycleStep.StepIndex"/> de la sucursal, o 0 si no hay ciclo.</summary>
    Task<int> GetCycleLengthAsync(int branchId, CancellationToken cancellationToken = default);

    Task<LoyaltyCycleStep?> GetByBranchAndStepIndexAsync(int branchId, int stepIndex, CancellationToken cancellationToken = default);
}
