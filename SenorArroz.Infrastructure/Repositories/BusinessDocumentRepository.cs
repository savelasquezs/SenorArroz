using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public sealed class BusinessDocumentRepository : IBusinessDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessDocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<BusinessDocument>> GetPagedAsync(
        string? search,
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BusinessDocuments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, $"%{term}%") ||
                EF.Functions.ILike(x.OriginalFileName, $"%{term}%"));
        }

        var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy.Trim().ToLowerInvariant() switch
        {
            "updatedat" => descending
                ? query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Name)
                : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Name),
            "createdat" => descending
                ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Name)
                : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Name),
            _ => descending
                ? query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public Task<BusinessDocument?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _context.BusinessDocuments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<BusinessDocument?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default) =>
        _context.BusinessDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicId == publicId
                && _context.Tenants.Any(tenant => tenant.Id == EF.Property<int?>(x, "TenantId") && tenant.Status == Domain.Enums.TenantStatus.Active), cancellationToken);

    public async Task<BusinessDocument> CreateAsync(
        BusinessDocument document,
        CancellationToken cancellationToken = default)
    {
        _context.BusinessDocuments.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task<BusinessDocument> UpdateAsync(
        BusinessDocument document,
        CancellationToken cancellationToken = default)
    {
        _context.BusinessDocuments.Update(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task DeleteAsync(BusinessDocument document, CancellationToken cancellationToken = default)
    {
        _context.BusinessDocuments.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
