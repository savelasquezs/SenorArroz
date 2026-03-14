using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BankTransfers.Commands;

public class CreateBankTransferHandler : IRequestHandler<CreateBankTransferCommand, BankTransferDto>
{
    private readonly IBankTransferRepository _bankTransferRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateBankTransferHandler(
        IBankTransferRepository bankTransferRepository,
        IBankRepository bankRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _bankTransferRepository = bankTransferRepository;
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankTransferDto> Handle(CreateBankTransferCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a 0");

        if (request.FromBankId == request.ToBankId)
            throw new BusinessException("El banco origen y destino no pueden ser el mismo");

        var fromBank = await _bankRepository.GetByIdAsync(request.FromBankId);
        if (fromBank == null)
            throw new BusinessException("El banco origen no existe");

        var toBank = await _bankRepository.GetByIdAsync(request.ToBankId);
        if (toBank == null)
            throw new BusinessException("El banco destino no existe");

        if (fromBank.BranchId != toBank.BranchId)
            throw new BusinessException("Los bancos deben pertenecer a la misma sucursal");

        if (_currentUser.Role != "superadmin" && fromBank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para realizar transferencias en esta sucursal");

        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");

        var bankTransfer = new BankTransfer
        {
            FromBankId = request.FromBankId,
            ToBankId = request.ToBankId,
            Amount = request.Amount,
            Note = request.Note,
            CreatedById = _currentUser.Id
        };

        var created = await _bankTransferRepository.CreateAsync(bankTransfer);
        return _mapper.Map<BankTransferDto>(created);
    }
}
