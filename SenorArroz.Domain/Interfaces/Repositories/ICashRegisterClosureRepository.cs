using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface ICashRegisterClosureRepository
{
    Task<CashRegisterClosure?> GetLastByBranchAsync(int branchId);
    Task<CashRegisterClosure?> GetByIdAsync(int id);
    Task<PagedResult<CashRegisterClosure>> GetPagedAsync(
        int? branchId,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 10,
        string sortBy = "closedAt",
        string sortOrder = "desc");
    Task<CashRegisterClosure> CreateAsync(CashRegisterClosure closure);
}
