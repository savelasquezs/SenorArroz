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
            Active = request.Active,
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
