// SenorArroz.Application/Features/AppPayments/Queries/GetAppPaymentByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Queries;

public class GetAppPaymentByIdHandler : IRequestHandler<GetAppPaymentByIdQuery, AppPaymentDto?>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetAppPaymentByIdHandler(IAppPaymentRepository appPaymentRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<AppPaymentDto?> Handle(GetAppPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id);
        
        if (appPayment == null)
            return null;

        // Check if user has access to this app payment's branch
        if (_currentUser.Role != "superadmin" && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            return null;

        return _mapper.Map<AppPaymentDto>(appPayment);
    }
}
