using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Customers.Queries;

public sealed class GetCustomerSuggestionsQuery : IRequest<CustomerSuggestionPageDto>
{
    public string Search { get; set; } = "";
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class CustomerSuggestionDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? WhatsAppUsername { get; set; }
    public List<string> BillingCompanies { get; set; } = [];
}

public sealed record CustomerSuggestionPageDto(IReadOnlyList<CustomerSuggestionDto> Items, int Page, bool HasMore);

/// <summary>Bounded autocomplete, without addresses, order statistics, loyalty or COUNT(*).</summary>
public sealed class GetCustomerSuggestionsHandler(IApplicationDbContext db, ICurrentUser current)
    : IRequestHandler<GetCustomerSuggestionsQuery, CustomerSuggestionPageDto>
{
    public const int PageSize = 10;

    public async Task<CustomerSuggestionPageDto> Handle(GetCustomerSuggestionsQuery request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!current.IsAuthenticated || current.TenantId <= 0)
            throw new UnauthorizedAccessException();
        var branchId = Roles.IsSuperadmin(current.Role) ? request.BranchId : current.BranchId;
        if (branchId is null or <= 0) throw new UnauthorizedAccessException();
        var term = (request.Search ?? "").Trim();
        if (term.Length > 120) throw new BusinessException("La búsqueda no puede superar 120 caracteres.");
        if (request.Page is < 1 or > 100000) throw new BusinessException("Página de búsqueda no válida.");
        if (term.Length < 2) return new([], request.Page, false);

        // Keep the existing lookup semantics: exact mobile identity is tenant-wide;
        // names, partial phones, usernames and billing-company matches stay branch-scoped.
        var mobile = ColombianPhoneNormalizer.TryNormalize(term, out var normalized) ? normalized : null;
        var companyPattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var companyCustomers = db.CustomerBillingRecipients.AsNoTracking()
            .Where(r => r.TenantId == current.TenantId && r.Profile != null
                && r.Profile.TenantId == current.TenantId
                && (EF.Functions.ILike(r.Profile.Name, companyPattern)
                    || r.Profile.TradeName != null && EF.Functions.ILike(r.Profile.TradeName, companyPattern)
                    || EF.Functions.ILike(r.Profile.Identification, companyPattern)))
            .Select(r => r.CustomerId);
        var query = db.Customers.AsNoTracking().Where(c => c.TenantId == current.TenantId && c.Active);
        if (mobile is not null)
        {
            query = query.Where(c => c.Phones.Any(p => p.Active && p.PhoneNormalized == mobile)
                || c.BranchId == branchId && companyCustomers.Contains(c.Id));
        }
        else
        {
            var pattern = $"%{term}%";
            var usernamePattern = $"%{term.TrimStart('@')}%";
            query = query.Where(c => c.BranchId == branchId && (
                EF.Functions.ILike(c.Name, pattern)
                || c.Phone1 != null && EF.Functions.ILike(c.Phone1, pattern)
                || c.Phone2 != null && EF.Functions.ILike(c.Phone2, pattern)
                || c.WhatsAppUsername != null && EF.Functions.ILike(c.WhatsAppUsername, usernamePattern)
                || companyCustomers.Contains(c.Id)));
        }

        var lower = term.ToLowerInvariant();
        var username = term.TrimStart('@').ToLowerInvariant();
        var rows = await query
            .OrderByDescending(c => mobile != null && c.Phones.Any(p => p.Active && p.PhoneNormalized == mobile))
            .ThenByDescending(c => c.Name.ToLower() == lower || c.Phone1 == term || c.Phone2 == term
                || c.WhatsAppUsername != null && (c.WhatsAppUsername.ToLower() == username || c.WhatsAppUsername.ToLower() == "@" + username))
            .ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((request.Page - 1) * PageSize).Take(PageSize + 1)
            .Select(c => new CustomerSuggestionDto { Id = c.Id, BranchId = c.BranchId, Name = c.Name,
                Phone1 = c.Phone1, Phone2 = c.Phone2, WhatsAppUsername = c.WhatsAppUsername })
            .ToListAsync(ct);
        var hasMore = rows.Count > PageSize;
        if (hasMore) rows.RemoveAt(PageSize);
        if (rows.Count == 0) return new(rows, request.Page, false);

        // One bounded query for labels, not one query for every customer.
        var ids = rows.Select(c => c.Id).ToArray();
        var companies = await db.CustomerBillingRecipients.AsNoTracking()
            .Where(r => r.TenantId == current.TenantId && ids.Contains(r.CustomerId)
                && r.Profile != null && r.Profile.TenantId == current.TenantId)
            .Select(r => new { r.CustomerId, Name = r.Profile!.TradeName ?? r.Profile.Name })
            .Distinct().ToListAsync(ct);
        var labels = companies.ToLookup(r => r.CustomerId, r => r.Name);
        foreach (var row in rows) row.BillingCompanies = labels[row.Id].OrderBy(name => name).ToList();
        return new(rows, request.Page, hasMore);
    }
}
