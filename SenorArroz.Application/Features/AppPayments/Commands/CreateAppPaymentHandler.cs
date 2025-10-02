// SenorArroz.Application/Features/AppPayments/Commands/CreateAppPaymentHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class CreateAppPaymentHandler : IRequestHandler<CreateAppPaymentCommand, AppPaymentDto>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IAppRepository _appRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateAppPaymentHandler(
        IAppPaymentRepository appPaymentRepository,
        IAppRepository appRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _appRepository = appRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<AppPaymentDto> Handle(CreateAppPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate app exists
        var app = await _appRepository.GetByIdAsync(request.AppId);
        if (app == null)
            throw new BusinessException("La app especificada no existe");

        // Check if user has access to this app's branch
        if (_currentUser.Role != "superadmin" && app.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para crear pagos en esta app");

        var appPayment = new AppPayment
        {
            OrderId = request.OrderId,
            AppId = request.AppId,
            Amount = request.Amount
        };

        var createdAppPayment = await _appPaymentRepository.CreateAsync(appPayment);
        return _mapper.Map<AppPaymentDto>(createdAppPayment);
    }
}
