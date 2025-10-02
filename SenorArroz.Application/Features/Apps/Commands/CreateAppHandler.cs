// SenorArroz.Application/Features/Apps/Commands/CreateAppHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Apps.Commands;

public class CreateAppHandler : IRequestHandler<CreateAppCommand, AppDto>
{
    private readonly IAppRepository _appRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateAppHandler(
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

    public async Task<AppDto> Handle(CreateAppCommand request, CancellationToken cancellationToken)
    {
        // Validate bank exists
        var bank = await _bankRepository.GetByIdAsync(request.BankId);
        if (bank == null)
            throw new BusinessException("El banco especificado no existe");

        // Check if user has access to this bank's branch
        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear apps en este banco");

        // Check if app name already exists in this bank
        if (await _appRepository.NameExistsInBankAsync(request.Name, request.BankId))
            throw new BusinessException("Ya existe una app con este nombre en el banco especificado");

        var app = new App
        {
            BankId = request.BankId,
            Name = request.Name,
            ImageUrl = request.ImageUrl,
            Active = request.Active
        };

        var createdApp = await _appRepository.CreateAsync(app);
        var appDto = _mapper.Map<AppDto>(createdApp);

        // Initialize stats for new app
        appDto.TotalPayments = 0;
        appDto.UnsettledPayments = 0;
        appDto.TotalPaymentsCount = 0;
        appDto.UnsettledPaymentsCount = 0;

        return appDto;
    }
}
