// SenorArroz.Application/Features/BankPayments/Queries/GetUnverifiedBankPaymentsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Queries;

public class GetUnverifiedBankPaymentsHandler : IRequestHandler<GetUnverifiedBankPaymentsQuery, IEnumerable<BankPaymentDto>>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IMapper _mapper;
    private readonly IBranchContext _branchContext;

    public GetUnverifiedBankPaymentsHandler(
        IBankPaymentRepository bankPaymentRepository,
        IMapper mapper,
        IBranchContext branchContext)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _mapper = mapper;
        _branchContext = branchContext;
    }

    public async Task<IEnumerable<BankPaymentDto>> Handle(GetUnverifiedBankPaymentsQuery request, CancellationToken cancellationToken)
    {
        var unverifiedPayments = await _bankPaymentRepository.GetUnverifiedAsync(cancellationToken);
        
        var bankPaymentDtos = new List<BankPaymentDto>();

        var branchId = _branchContext.RequireBranch();
        foreach (var bankPayment in unverifiedPayments.Where(x => x.Bank.BranchId == branchId))
        {
            var bankPaymentDto = _mapper.Map<BankPaymentDto>(bankPayment);
            bankPaymentDtos.Add(bankPaymentDto);
        }

        return bankPaymentDtos;
    }
}
