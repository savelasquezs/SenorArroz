using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Helpers;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetBranchInformalLoansHandler : IRequestHandler<GetBranchInformalLoansQuery, List<BranchInformalLoanDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetBranchInformalLoansHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<BranchInformalLoanDto>> Handle(GetBranchInformalLoansQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var scope = (request.Scope ?? "active").Trim().ToLowerInvariant();

        if (scope != "active" && !Roles.IsAdminOrSuperadmin(_currentUser.Role))
            throw new BusinessException("Solo administradores pueden consultar préstamos inactivos");

        var query = _context.BranchInformalLoans
            .AsNoTracking()
            .Where(l => l.BranchId == branchId);

        query = scope switch
        {
            "inactive" => query.Where(l => l.DeactivatedAt != null),
            "all" => query,
            _ => query.Where(l => l.DeactivatedAt == null)
        };
        query = BranchInformalLoanFilter.Apply(query, request.Source, request.BankId);

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new BranchInformalLoanDto
            {
                Id = l.Id,
                BranchId = l.BranchId,
                BankId = l.BankId,
                BankName = l.Bank != null ? l.Bank.Name : null,
                Concept = l.Concept,
                Amount = l.Amount,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                CreatedById = l.CreatedById,
                CreatedByName = l.CreatedBy.Name,
                DeactivatedAt = l.DeactivatedAt,
                DeactivatedById = l.DeactivatedById,
                DeactivatedByName = l.DeactivatedBy != null ? l.DeactivatedBy.Name : null,
                DeactivationNotes = l.DeactivationNotes,
                TotalPaid = l.Payments.Sum(p => p.Amount),
                PaymentsCount = l.Payments.Count
            })
            .ToListAsync(cancellationToken);
    }
}
