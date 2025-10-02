// SenorArroz.Application/Features/AppPayments/Commands/UnsettleAppPaymentHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class UnsettleAppPaymentHandler : IRequestHandler<UnsettleAppPaymentCommand, bool>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly ICurrentUser _currentUser;

    public UnsettleAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IBankPaymentRepository bankPaymentRepository,
        ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(UnsettleAppPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate app payment exists
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id);
        if (appPayment == null)
            return false;

        // Check if user has access to this app payment's branch
        if (_currentUser.Role != "superadmin" && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para desliquidar este pago");

        // Check if not settled
        if (!appPayment.IsSetted)
            return true; // Already unsettled

        // Find and delete the corresponding bank payment
        // We need to find the bank payment that was created when this app payment was settled
        var bankPayments = await _bankPaymentRepository.GetByOrderIdAsync(appPayment.OrderId);
        var correspondingBankPayment = bankPayments
            .FirstOrDefault(bp => bp.BankId == appPayment.App.BankId && 
                                 bp.Amount == appPayment.Amount &&
                                 bp.VerifiedAt == null); // Only unverified bank payments can be deleted

        if (correspondingBankPayment != null)
        {
            // Delete the corresponding bank payment
            await _bankPaymentRepository.DeleteAsync(correspondingBankPayment.Id);
        }

        // Mark app payment as unsettled
        return await _appPaymentRepository.UnsettlePaymentsAsync(new[] { request.Id });
    }
}
