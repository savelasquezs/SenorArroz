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

    public UnverifyBankPaymentHandler(IBankPaymentRepository bankPaymentRepository, ICurrentUser currentUser)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(UnverifyBankPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate bank payment exists
        var bankPayment = await _bankPaymentRepository.GetByIdAsync(request.Id);
        if (bankPayment == null)
            return false;

        // Check if user has access to this bank payment's branch
        if (_currentUser.Role != "superadmin" && bankPayment.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para desverificar este pago");

        return await _bankPaymentRepository.UnverifyPaymentAsync(request.Id);
    }
}
