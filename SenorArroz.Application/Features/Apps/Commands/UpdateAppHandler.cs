// SenorArroz.Application/Features/Apps/Commands/UpdateAppHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Apps.Commands;

public class UpdateAppHandler : IRequestHandler<UpdateAppCommand, AppDto>
{
    private readonly IAppRepository _appRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public UpdateAppHandler(
        IAppRepository appRepository,
        IBankRepository bankRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _appRepository = appRepository;
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<AppDto> Handle(UpdateAppCommand request, CancellationToken cancellationToken)
    {
        // Validate app exists
        var existingApp = await _appRepository.GetByIdAsync(request.Id);
        if (existingApp == null)
            throw new BusinessException("La app especificada no existe");

        // Check if user has access to this app's branch
        if (_currentUser.Role != "superadmin" && existingApp.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar esta app");

        // Validate bank exists
        var bank = await _bankRepository.GetByIdAsync(request.BankId);
        if (bank == null)
            throw new BusinessException("El banco especificado no existe");

        // Check if user has access to the new bank's branch
        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para asignar apps a este banco");

        // Check if app name already exists in this bank (excluding current app)
        if (await _appRepository.NameExistsInBankAsync(request.Name, request.BankId, request.Id))
            throw new BusinessException("Ya existe una app con este nombre en el banco especificado");

        // Update app properties
        existingApp.BankId = request.BankId;
        existingApp.Name = request.Name;
        existingApp.ImageUrl = request.ImageUrl;
        existingApp.Active = request.Active;

        var updatedApp = await _appRepository.UpdateAsync(existingApp);
        var appDto = _mapper.Map<AppDto>(updatedApp);

        // Add current statistics
        appDto.TotalPayments = await _appRepository.GetTotalAppPaymentsAsync(updatedApp.Id);
        appDto.UnsettledPayments = await _appRepository.GetUnsettledAppPaymentsAsync(updatedApp.Id);
        appDto.TotalPaymentsCount = await _appRepository.GetTotalAppPaymentsCountAsync(updatedApp.Id);
        appDto.UnsettledPaymentsCount = await _appRepository.GetUnsettledAppPaymentsCountAsync(updatedApp.Id);

        return appDto;
    }
}
