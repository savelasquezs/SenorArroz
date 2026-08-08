using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public sealed class UnverifyTodayBankPaymentsHandler : IRequestHandler<UnverifyTodayBankPaymentsCommand, int>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;

    public UnverifyTodayBankPaymentsHandler(
        IBankPaymentRepository bankPaymentRepository,
        IBankRepository bankRepository,
        IBranchContext branchContext,
        IClock clock)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _bankRepository = bankRepository;
        _branchContext = branchContext;
        _clock = clock;
    }

    public async Task<int> Handle(
        UnverifyTodayBankPaymentsCommand request,
        CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdAsync(request.BankId, cancellationToken);
        if (bank == null)
            throw new NotFoundException("Banco no encontrado");

        _branchContext.EnsureAccess(bank.BranchId);

        var todayColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(_clock.UtcNow).Date;
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(
            todayColombia,
            todayColombia);

        return await _bankPaymentRepository.UnverifyPaymentsForBankInPeriodAsync(
            request.BankId,
            fromUtc,
            toUtc,
            bank.BranchId,
            cancellationToken);
    }
}
