// SenorArroz.Application/Features/Banks/Commands/UpdateBankHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Commands;

public class UpdateBankHandler : IRequestHandler<UpdateBankCommand, BankDto>
{
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public UpdateBankHandler(
        IBankRepository bankRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<BankDto> Handle(UpdateBankCommand request, CancellationToken cancellationToken)
    {
        // Validate bank exists
        var existingBank = await _bankRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingBank == null)
            throw new BusinessException("El banco especificado no existe");
        _branchContext.EnsureAccess(existingBank.BranchId);

        // Check if user has access to this bank's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && existingBank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar este banco");

        // Check if bank name already exists in this branch (excluding current bank)
        if (await _bankRepository.NameExistsInBranchAsync(request.Name, existingBank.BranchId, request.Id))
            throw new BusinessException("Ya existe un banco con este nombre en la sucursal especificada");

        // Update bank properties
        existingBank.Name = request.Name;
        existingBank.ImageUrl = request.ImageUrl;
        existingBank.Active = request.Active;
        if (request.Type.HasValue)
            existingBank.Type = request.Type.Value;

        var updatedBank = await _bankRepository.UpdateAsync(existingBank, cancellationToken);
        var bankDto = _mapper.Map<BankDto>(updatedBank);

        // Add current statistics
        bankDto.TotalApps = await _bankRepository.GetTotalAppsAsync(updatedBank.Id, cancellationToken);
        bankDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(updatedBank.Id, cancellationToken);
        bankDto.CurrentBalance = await _bankRepository.GetCurrentBalanceAsync(updatedBank.Id, cancellationToken);

        return bankDto;
    }
}
