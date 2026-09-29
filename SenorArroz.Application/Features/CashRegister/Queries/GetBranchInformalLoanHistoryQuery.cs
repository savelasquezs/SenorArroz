using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetBranchInformalLoanHistoryQuery : IRequest<PagedResult<BranchInformalLoanHistoryDto>>
{
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetBranchInformalLoanHistoryHandler : IRequestHandler<GetBranchInformalLoanHistoryQuery, PagedResult<BranchInformalLoanHistoryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetBranchInformalLoanHistoryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<BranchInformalLoanHistoryDto>> Handle(GetBranchInformalLoanHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
            throw new BusinessException("Solo administradores pueden consultar el historial de préstamos");

        var branchId = request.BranchId ?? _currentUser.BranchId;
        if (!Roles.IsSuperadmin(_currentUser.Role) && branchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permiso para esta sucursal");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var query = _context.BranchInformalLoans.AsNoTracking().Where(x => x.BranchId == branchId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BranchInformalLoanHistoryDto
            {
                Id = x.Id,
                BranchId = x.BranchId,
                Concept = x.Concept,
                Amount = x.Amount,
                InitialAmount = x.Amount + x.Payments.Sum(p => p.Amount),
                TotalPaid = x.Payments.Sum(p => p.Amount),
                PaymentsCount = x.Payments.Count,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                CreatedById = x.CreatedById,
                CreatedByName = x.CreatedBy.Name,
                DeactivatedAt = x.DeactivatedAt,
                DeactivatedById = x.DeactivatedById,
                DeactivatedByName = x.DeactivatedBy != null ? x.DeactivatedBy.Name : null,
                DeactivationNotes = x.DeactivationNotes,
                Payments = x.Payments
                    .OrderByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id)
                    .Select(p => new BranchInformalLoanPaymentDto
                    {
                        Id = p.Id,
                        Kind = p.Kind,
                        Amount = p.Amount,
                        BalanceBefore = p.BalanceBefore,
                        BalanceAfter = p.BalanceAfter,
                        Notes = p.Notes,
                        CreatedAt = p.CreatedAt,
                        CreatedById = p.CreatedById,
                        CreatedByName = p.CreatedBy.Name,
                        ExpenseHeaderId = p.ExpenseHeaderId
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<BranchInformalLoanHistoryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
