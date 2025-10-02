// SenorArroz.Application/Features/Banks/Commands/CreateBankHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Commands;

public class CreateBankHandler : IRequestHandler<CreateBankCommand, BankDto>
{
    private readonly IBankRepository _bankRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateBankHandler(
        IBankRepository bankRepository,
        IBranchRepository branchRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _bankRepository = bankRepository;
        _branchRepository = branchRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankDto> Handle(CreateBankCommand request, CancellationToken cancellationToken)
    {
        // Determine branch
        int branchId;

        if (_currentUser.Role == "superadmin")
        {
            // Superadmin can specify branch or needs to provide it
            if (request.BranchId <= 0)
            {
                throw new BusinessException("Superadmin debe especificar la sucursal");
            }
            branchId = request.BranchId;
        }
        else if (_currentUser.Role == "admin")
        {
            // Admin uses their branch
            branchId = _currentUser.BranchId;
        }
        else
        {
            throw new BusinessException("No tienes permisos para crear bancos");
        }

        // Validate branch exists
        if (!await _branchRepository.ExistsAsync(branchId))
        {
            throw new BusinessException("La sucursal especificada no existe");
        }

        // Check if bank name already exists in this branch
        if (await _bankRepository.NameExistsInBranchAsync(request.Name, branchId))
        {
            throw new BusinessException("Ya existe un banco con este nombre en la sucursal especificada");
        }

        var bank = new Bank
        {
            BranchId = branchId,
            Name = request.Name,
            ImageUrl = request.ImageUrl,
            Active = request.Active
        };

        var createdBank = await _bankRepository.CreateAsync(bank);
        var bankDto = _mapper.Map<BankDto>(createdBank);

        // Initialize stats for new bank
        bankDto.TotalApps = 0;
        bankDto.ActiveApps = 0;
        bankDto.CurrentBalance = 0;

        return bankDto;
    }
}
