using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankExpenseBankPaymentsPagedHandler
    : IRequestHandler<GetBankExpenseBankPaymentsPagedQuery, PagedResult<ExpenseBankPaymentLineDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly IBankRepository _bankRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public GetBankExpenseBankPaymentsPagedHandler(
        IApplicationDbContext context,
        IBankRepository bankRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _context = context;
        _bankRepository = bankRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<PagedResult<ExpenseBankPaymentLineDto>?> Handle(
        GetBankExpenseBankPaymentsPagedQuery request,
        CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdAsync(request.BankId, cancellationToken);
        if (bank == null)
            return null;
        var branchId = _branchContext.RequireBranch(request.BranchId);
        _branchContext.EnsureAccess(bank.BranchId);
        if (bank.BranchId != branchId)
            throw new SenorArroz.Domain.Exceptions.BranchScopeMismatchException();

        if (!Roles.IsSuperadmin(_currentUser.Role) && bank.BranchId != _currentUser.BranchId)
            return null;

        if (Roles.IsCashier(_currentUser.Role) && (bank.Type == BankType.CashVault || bank.Type == BankType.RealVault))
            return null;

        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(request.FromDate, request.ToDate);

        var query = _context.ExpenseBankPayments
            .AsNoTracking()
            .Where(ebp => ebp.BankId == request.BankId
                && ebp.CreatedAt >= fromUtc && ebp.CreatedAt <= toUtc);

        query = query.Where(ebp => ebp.ExpenseHeader.BranchId == branchId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(ebp => ebp.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(ebp => new ExpenseBankPaymentLineDto
            {
                Id = ebp.Id,
                Amount = ebp.Amount,
                CreatedAt = ebp.CreatedAt,
                ExpenseHeaderId = ebp.ExpenseHeaderId,
                BranchId = ebp.ExpenseHeader.BranchId,
                SupplierName = ebp.ExpenseHeader.Supplier.Name
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ExpenseBankPaymentLineDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
        };
    }
}
