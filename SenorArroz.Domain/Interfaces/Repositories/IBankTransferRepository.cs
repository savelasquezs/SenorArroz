using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBankTransferRepository
{
    Task<BankTransfer> CreateAsync(BankTransfer transfer, CancellationToken cancellationToken = default);
    Task<PagedResult<BankTransfer>> GetPagedAsync(
        int? branchId = null,
        int? fromBankId = null,
        int? toBankId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc",
        CancellationToken cancellationToken = default);
}
