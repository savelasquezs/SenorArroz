// SenorArroz.Application/Features/AppPayments/Commands/SettleAppPaymentHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class SettleAppPaymentHandler : IRequestHandler<SettleAppPaymentCommand, bool>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public SettleAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IBankPaymentRepository bankPaymentRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _appPaymentRepository = appPaymentRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<bool> Handle(SettleAppPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate app payment exists
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (appPayment == null)
            return false;
        _branchContext.EnsureAccess(appPayment.App.Bank.BranchId);

        // Check if user has access to this app payment's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para liquidar este pago");

        // Check if already settled
        if (appPayment.IsSetted)
            return true; // Already settled
        if (appPayment.IsReversed)
            throw new BusinessException("No se puede liquidar un pago revertido.");
        if (appPayment.ExpectedNetAmount.HasValue)
            throw new BusinessException(
                "Los pagos Rappi deben liquidarse indicando el valor real consignado.");

        // Mark app payment as settled
        var settled = await _appPaymentRepository.SettlePaymentsAsync(new[] { request.Id }, cancellationToken);
        if (!settled)
            return false;

        // Create corresponding bank payment as per specifications
        var bankPayment = new BankPayment
        {
            OrderId = appPayment.OrderId,
            BankId = appPayment.App.BankId, // Use the same bank as the app
            Amount = appPayment.Amount,
            IsAppSettlement = true,
            AppSettlementSourcePaymentIds = AppSettlementBankPaymentSourceIds.Serialize(new[] { appPayment.Id })
        };

        await _bankPaymentRepository.CreateAsync(bankPayment, cancellationToken);

        return true;
    }
}
