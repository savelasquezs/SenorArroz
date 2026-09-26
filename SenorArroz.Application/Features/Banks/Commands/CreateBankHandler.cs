// SenorArroz.Application/Features/Banks/Commands/CreateBankHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Commands;

public class CreateBankHandler : IRequestHandler<CreateBankCommand, BankDto>
{
    private readonly IBankRepository _bankRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public CreateBankHandler(
        IBankRepository bankRepository,
        IBranchRepository branchRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _bankRepository = bankRepository;
        _branchRepository = branchRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<BankDto> Handle(CreateBankCommand request, CancellationToken cancellationToken)
    {
        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
        {
            throw new BusinessException("No tienes permisos para crear bancos");
        }
        var branchId = _branchContext.RequireBranch(request.BranchId);

        // Validate branch exists
        if (!await _branchRepository.ExistsAsync(branchId))
        {
            throw new BusinessException("La sucursal especificada no existe");
        }

        if (request.Type is not BankType.Normal and not BankType.CashVault)
        {
            throw new BusinessException("Desde esta ruta solo se pueden crear bancos normales o Caja Mayor Efectivo");
        }

        var bankName = request.Name.Trim();
        var imageUrl = request.ImageUrl;
        var active = request.Active;

        if (request.Type == BankType.CashVault)
        {
            if (await _bankRepository.TypeExistsInBranchAsync(BankType.CashVault, branchId, cancellationToken: cancellationToken))
            {
                throw new BusinessException("Esta sucursal ya tiene una Caja Mayor Efectivo");
            }

            // La Caja Mayor tiene identidad fija: no es un banco operativo normal.
            bankName = "Caja Mayor Efectivo";
            imageUrl = null;
            active = true;
        }

        // Check if bank name already exists in this branch
        if (await _bankRepository.NameExistsInBranchAsync(bankName, branchId, cancellationToken: cancellationToken))
        {
            throw new BusinessException("Ya existe un banco con este nombre en la sucursal especificada");
        }

        var bank = new Bank
        {
            BranchId = branchId,
            Name = bankName,
            ImageUrl = imageUrl,
            Active = active,
            Type = request.Type
        };

        var createdBank = await _bankRepository.CreateAsync(bank, cancellationToken);
        var bankDto = _mapper.Map<BankDto>(createdBank);

        // Initialize stats for new bank
        bankDto.TotalApps = 0;
        bankDto.ActiveApps = 0;
        bankDto.CurrentBalance = 0;

        return bankDto;
    }
}
