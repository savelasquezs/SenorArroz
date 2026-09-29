using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CreateBranchInformalLoanPaymentCommand : IRequest<BranchInformalLoanPaymentDto>
{
    public int LoanId { get; set; }
    public int? BranchId { get; set; }
    public CreateBranchInformalLoanPaymentDto Dto { get; set; } = null!;
}

public class CreateBranchInformalLoanPaymentHandler : IRequestHandler<CreateBranchInformalLoanPaymentCommand, BranchInformalLoanPaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public CreateBranchInformalLoanPaymentHandler(IApplicationDbContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<BranchInformalLoanPaymentDto> Handle(CreateBranchInformalLoanPaymentCommand request, CancellationToken cancellationToken)
    {
        var branchId = request.BranchId ?? _currentUser.BranchId;
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            var loan = await _context.BranchInformalLoans
                .SingleOrDefaultAsync(x => x.Id == request.LoanId && x.BranchId == branchId, cancellationToken);
            if (loan is null)
                throw new BusinessException("Préstamo no encontrado");

            var payment = BranchInformalLoanPaymentHelper.Apply(
                _context,
                _currentUser,
                _clock,
                loan,
                request.Dto.Amount,
                BranchInformalLoanPaymentKind.Cash,
                request.Dto.Notes);

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new BranchInformalLoanPaymentDto
            {
                Id = payment.Id,
                Kind = payment.Kind,
                Amount = payment.Amount,
                BalanceBefore = payment.BalanceBefore,
                BalanceAfter = payment.BalanceAfter,
                Notes = payment.Notes,
                CreatedAt = payment.CreatedAt,
                CreatedById = payment.CreatedById,
                ExpenseHeaderId = payment.ExpenseHeaderId
            };
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
