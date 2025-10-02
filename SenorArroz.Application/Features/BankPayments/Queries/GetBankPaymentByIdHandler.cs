// SenorArroz.Application/Features/BankPayments/Queries/GetBankPaymentByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Queries;

public class GetBankPaymentByIdHandler : IRequestHandler<GetBankPaymentByIdQuery, BankPaymentDto?>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetBankPaymentByIdHandler(IBankPaymentRepository bankPaymentRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankPaymentDto?> Handle(GetBankPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var bankPayment = await _bankPaymentRepository.GetByIdAsync(request.Id);
        
        if (bankPayment == null)
            return null;

        // Check if user has access to this bank payment's branch
        if (_currentUser.Role != "superadmin" && bankPayment.Bank.BranchId != _currentUser.BranchId)
            return null;

        return _mapper.Map<BankPaymentDto>(bankPayment);
    }
}
