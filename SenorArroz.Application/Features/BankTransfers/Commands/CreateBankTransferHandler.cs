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

        var fromId = request.FromBankId;
        var toId = request.ToBankId;

        if (!fromId.HasValue && !toId.HasValue)
            throw new BusinessException("Indica banco origen y/o destino; un extremo puede ser efectivo de caja.");

        if (fromId.HasValue && toId.HasValue && fromId.Value == toId.Value)
            throw new BusinessException("El banco origen y destino no pueden ser el mismo");

        Bank? fromBank = null;
        Bank? toBank = null;

        if (fromId.HasValue)
        {
            fromBank = await _bankRepository.GetByIdAsync(fromId.Value, cancellationToken);
            if (fromBank == null)
                throw new BusinessException("El banco origen no existe");
        }

        if (toId.HasValue)
        {
            toBank = await _bankRepository.GetByIdAsync(toId.Value, cancellationToken);
            if (toBank == null)
                throw new BusinessException("El banco destino no existe");
        }

        if (fromBank != null && toBank != null && fromBank.BranchId != toBank.BranchId)
            throw new BusinessException("Los bancos deben pertenecer a la misma sucursal");

        var branchIdForPerm = fromBank?.BranchId ?? toBank!.BranchId;
        if (!Roles.IsSuperadmin(_currentUser.Role) && branchIdForPerm != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para realizar transferencias en esta sucursal");

        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");

        var bankTransfer = new BankTransfer
        {
            FromBankId = fromId,
            ToBankId = toId,
            Amount = request.Amount,
            Note = request.Note,
            CreatedById = _currentUser.Id
        };

        var created = await _bankTransferRepository.CreateAsync(bankTransfer, cancellationToken);
        return _mapper.Map<BankTransferDto>(created);
    }
}
