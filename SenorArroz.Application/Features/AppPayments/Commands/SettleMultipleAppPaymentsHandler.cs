// SenorArroz.Application/Features/AppPayments/Commands/SettleMultipleAppPaymentsHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class SettleMultipleAppPaymentsHandler : IRequestHandler<SettleMultipleAppPaymentsCommand, bool>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly ICurrentUser _currentUser;

    public SettleMultipleAppPaymentsHandler(
        IAppPaymentRepository appPaymentRepository,
        IBankPaymentRepository bankPaymentRepository,
        ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(SettleMultipleAppPaymentsCommand request, CancellationToken cancellationToken)
    {
        if (request.PaymentIds == null || !request.PaymentIds.Any())
            throw new BusinessException("Se requiere al menos un ID de pago");

        var appPayments = new List<AppPayment>();
        var totalAmount = 0m;
        var firstOrderId = 0;
        var firstBankId = 0;

        // Validate all app payments exist and user has access
        foreach (var paymentId in request.PaymentIds)
        {
            var appPayment = await _appPaymentRepository.GetByIdAsync(paymentId);
            if (appPayment == null)
                throw new BusinessException($"El pago con ID {paymentId} no existe");

            // Check if user has access to this app payment's branch
            if (_currentUser.Role != "superadmin" && appPayment.App.Bank.BranchId != _currentUser.BranchId)
                throw new BusinessException($"No tienes permisos para liquidar el pago con ID {paymentId}");

            // Check if already settled
            if (appPayment.IsSetted)
                continue; // Skip already settled payments

            appPayments.Add(appPayment);
            totalAmount += appPayment.Amount;
            
            // Set first order and bank for the bank payment
            if (firstOrderId == 0)
            {
                firstOrderId = appPayment.OrderId;
                firstBankId = appPayment.App.BankId;
            }
        }

        if (!appPayments.Any())
            return true; // All payments already settled

        // Mark all app payments as settled
        var settledPaymentIds = appPayments.Select(p => p.Id).ToArray();
        var settled = await _appPaymentRepository.SettlePaymentsAsync(settledPaymentIds);
        if (!settled)
            return false;

        // Create single bank payment with total amount as per specifications
        var bankPayment = new BankPayment
        {
            OrderId = firstOrderId, // Use the first order ID as reference
            BankId = firstBankId,   // Use the first bank ID (assuming all apps belong to same bank)
            Amount = totalAmount
        };

        await _bankPaymentRepository.CreateAsync(bankPayment);

        return true;
    }
}
