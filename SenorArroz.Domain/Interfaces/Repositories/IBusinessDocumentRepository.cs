using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBusinessDocumentRepository
{
    Task<PagedResult<BusinessDocument>> GetPagedAsync(
        string? search,
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        CancellationToken cancellationToken = default);

    Task<BusinessDocument?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<BusinessDocument?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);
    Task<BusinessDocument> CreateAsync(BusinessDocument document, CancellationToken cancellationToken = default);
    Task<BusinessDocument> UpdateAsync(BusinessDocument document, CancellationToken cancellationToken = default);
    Task DeleteAsync(BusinessDocument document, CancellationToken cancellationToken = default);
}
