using SenorArroz.Application.Common.Helpers;
// SenorArroz.Application/Features/BankPayments/Commands/UnverifyBankPaymentHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class UnverifyBankPaymentHandler : IRequestHandler<UnverifyBankPaymentCommand, bool>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public UnverifyBankPaymentHandler(
        IBankPaymentRepository bankPaymentRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(UnverifyBankPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate bank payment exists
        var bankPayment = await _bankPaymentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (bankPayment == null)
            return false;
        OrderAccessRules.EnsureAccess(bankPayment.Order, _currentUser, _branchContext);
        OrderAccessRules.EnsurePaymentBranch(bankPayment.Order, bankPayment.Bank.BranchId);

        // Check if user has access to this bank payment's branch

        return await _bankPaymentRepository.UnverifyPaymentAsync(request.Id, cancellationToken);
    }
}
