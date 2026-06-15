using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetCashVaultMovementsHandler : IRequestHandler<GetCashVaultMovementsQuery, PagedResult<CashVaultMovementDto>>
{
    private const int MaxPageSize = 100;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetCashVaultMovementsHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<CashVaultMovementDto>> Handle(GetCashVaultMovementsQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");

        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
            throw new BusinessException("Solo administradores pueden ver el historial de caja mayor");

        var branchId = request.BranchId ?? _currentUser.BranchId;
        if (branchId <= 0)
            throw new BusinessException("Sucursal no valida");

        if (!Roles.IsSuperadmin(_currentUser.Role) && branchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permiso para esta sucursal");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var query = _context.CashVaultMovements
            .AsNoTracking()
            .Where(m => m.BranchId == branchId && m.Bank.Type == BankType.CashVault);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new CashVaultMovementDto
            {
                Id = m.Id,
                BranchId = m.BranchId,
                BankId = m.BankId,
                BankName = m.Bank.Name,
                Kind = m.Kind,
                Amount = m.Amount,
                Note = m.Note,
                CreatedAt = m.CreatedAt,
                CreatedById = m.CreatedById,
                CreatedByName = m.CreatedBy.Name,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<CashVaultMovementDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
        };
    }
}
