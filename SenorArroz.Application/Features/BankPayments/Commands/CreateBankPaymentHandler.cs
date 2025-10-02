// SenorArroz.Application/Features/BankPayments/Commands/CreateBankPaymentHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class CreateBankPaymentHandler : IRequestHandler<CreateBankPaymentCommand, BankPaymentDto>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateBankPaymentHandler(
        IBankPaymentRepository bankPaymentRepository,
        IBankRepository bankRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankPaymentDto> Handle(CreateBankPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate bank exists
        var bank = await _bankRepository.GetByIdAsync(request.BankId);
        if (bank == null)
            throw new BusinessException("El banco especificado no existe");

        // Check if user has access to this bank's branch
        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear pagos en este banco");

        var bankPayment = new BankPayment
        {
            OrderId = request.OrderId,
            BankId = request.BankId,
            Amount = request.Amount
        };

        var createdBankPayment = await _bankPaymentRepository.CreateAsync(bankPayment);
        return _mapper.Map<BankPaymentDto>(createdBankPayment);
    }
}
