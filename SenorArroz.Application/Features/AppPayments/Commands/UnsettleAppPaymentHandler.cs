// SenorArroz.Application/Features/AppPayments/Commands/UnsettleAppPaymentHandler.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class UnsettleAppPaymentHandler : IRequestHandler<UnsettleAppPaymentCommand, bool>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public UnsettleAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(UnsettleAppPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate app payment exists
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (appPayment == null)
            return false;

        // Check if user has access to this app payment's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para desliquidar este pago");

        // Check if not settled
        if (!appPayment.IsSetted)
            return true; // Already unsettled

        var settlementBankPayment = await FindSettlementBankPaymentAsync(appPayment, cancellationToken);
        if (settlementBankPayment == null)
            throw new BusinessException("No se encontró el ingreso bancario creado por esta liquidación. No se desliquidó para evitar duplicar caja.");

        if (settlementBankPayment.IsVerified || settlementBankPayment.VerifiedAt.HasValue)
            throw new BusinessException("No se puede desliquidar este pago porque el ingreso bancario de la liquidación ya está verificado.");

        var trackedAppPayment = await _context.AppPayments
            .FirstOrDefaultAsync(ap => ap.Id == appPayment.Id, cancellationToken);
        if (trackedAppPayment == null)
            return false;

        var sourceIds = AppSettlementBankPaymentSourceIds.Parse(settlementBankPayment.AppSettlementSourcePaymentIds);
        var remainingSourceIds = sourceIds
            .Where(id => id != appPayment.Id)
            .ToList();

        if (settlementBankPayment.Amount < appPayment.Amount)
            throw new BusinessException("El ingreso bancario de la liquidación es menor al pago que se intenta desliquidar.");

        if (settlementBankPayment.Amount == appPayment.Amount)
        {
            _context.BankPayments.Remove(settlementBankPayment);
        }
        else
        {
            settlementBankPayment.Amount -= appPayment.Amount;
            settlementBankPayment.AppSettlementSourcePaymentIds = AppSettlementBankPaymentSourceIds.Serialize(remainingSourceIds);
        }

        trackedAppPayment.IsSetted = false;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<SenorArroz.Domain.Entities.BankPayment?> FindSettlementBankPaymentAsync(
        SenorArroz.Domain.Entities.AppPayment appPayment,
        CancellationToken cancellationToken)
    {
        var candidates = await _context.BankPayments
            .Where(bp => bp.IsAppSettlement
                && bp.BankId == appPayment.App.BankId
                && !bp.SourceReservationDepositId.HasValue)
            .OrderByDescending(bp => bp.CreatedAt)
            .ToListAsync(cancellationToken);

        var linked = candidates.FirstOrDefault(bp =>
            AppSettlementBankPaymentSourceIds.Parse(bp.AppSettlementSourcePaymentIds).Contains(appPayment.Id));
        if (linked != null)
            return linked;

        return candidates.FirstOrDefault(bp =>
            bp.OrderId == appPayment.OrderId
            && bp.Amount == appPayment.Amount
            && string.IsNullOrWhiteSpace(bp.AppSettlementSourcePaymentIds));
    }
}
