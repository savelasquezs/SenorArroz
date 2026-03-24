using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankDeliverymanTransferAdvancesPagedHandler
    : IRequestHandler<GetBankDeliverymanTransferAdvancesPagedQuery, PagedResult<DeliverymanBankAdvanceLineDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly IBankRepository _bankRepository;
    private readonly ICurrentUser _currentUser;

    public GetBankDeliverymanTransferAdvancesPagedHandler(
        IApplicationDbContext context,
        IBankRepository bankRepository,
        ICurrentUser currentUser)
    {
        _context = context;
        _bankRepository = bankRepository;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<DeliverymanBankAdvanceLineDto>?> Handle(
        GetBankDeliverymanTransferAdvancesPagedQuery request,
        CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdAsync(request.BankId);
        if (bank == null)
            return null;

        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            return null;

        if (_currentUser.Role == "cashier" && (bank.Type == BankType.CashVault || bank.Type == BankType.RealVault))
            return null;

        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(request.FromDate, request.ToDate);

        var query = _context.DeliverymanAdvances
            .AsNoTracking()
            .Where(a => a.BankId == request.BankId
                && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer
                && a.CreatedAt >= fromUtc && a.CreatedAt <= toUtc);

        if (_currentUser.Role != "superadmin")
            query = query.Where(a => a.BranchId == _currentUser.BranchId);
        else if (request.BranchId.HasValue && request.BranchId.Value > 0)
            query = query.Where(a => a.BranchId == request.BranchId.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new DeliverymanBankAdvanceLineDto
            {
                Id = a.Id,
                Amount = a.Amount,
                CreatedAt = a.CreatedAt,
                DeliverymanId = a.DeliverymanId,
                DeliverymanName = a.Deliveryman.Name,
                BranchId = a.BranchId,
                Notes = a.Notes
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<DeliverymanBankAdvanceLineDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
        };
    }
}
