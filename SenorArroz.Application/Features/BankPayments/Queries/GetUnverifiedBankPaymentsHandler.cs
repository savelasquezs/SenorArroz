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
    private readonly ICurrentUser _currentUser;

    public GetUnverifiedBankPaymentsHandler(IBankPaymentRepository bankPaymentRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<BankPaymentDto>> Handle(GetUnverifiedBankPaymentsQuery request, CancellationToken cancellationToken)
    {
        var unverifiedPayments = await _bankPaymentRepository.GetUnverifiedAsync();
        
        var bankPaymentDtos = new List<BankPaymentDto>();

        foreach (var bankPayment in unverifiedPayments)
        {
            // Check if user has access to this bank payment's branch
            if (_currentUser.Role != "superadmin" && bankPayment.Bank.BranchId != _currentUser.BranchId)
                continue;

            var bankPaymentDto = _mapper.Map<BankPaymentDto>(bankPayment);
            bankPaymentDtos.Add(bankPaymentDto);
        }

        return bankPaymentDtos;
    }
}
