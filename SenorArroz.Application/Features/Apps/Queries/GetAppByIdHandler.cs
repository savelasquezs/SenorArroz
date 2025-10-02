// SenorArroz.Application/Features/Apps/Queries/GetAppByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Apps.Queries;

public class GetAppByIdHandler : IRequestHandler<GetAppByIdQuery, AppDto?>
{
    private readonly IAppRepository _appRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetAppByIdHandler(IAppRepository appRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _appRepository = appRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<AppDto?> Handle(GetAppByIdQuery request, CancellationToken cancellationToken)
    {
        var app = await _appRepository.GetByIdAsync(request.Id);
        
        if (app == null)
            return null;

        // Check if user has access to this app's branch
        if (_currentUser.Role != "superadmin" && app.Bank.BranchId != _currentUser.BranchId)
            return null;

        var appDto = _mapper.Map<AppDto>(app);

        // Add additional data
        appDto.TotalPayments = await _appRepository.GetTotalAppPaymentsAsync(app.Id);
        appDto.UnsettledPayments = await _appRepository.GetUnsettledAppPaymentsAsync(app.Id);
        appDto.TotalPaymentsCount = await _appRepository.GetTotalAppPaymentsCountAsync(app.Id);
        appDto.UnsettledPaymentsCount = await _appRepository.GetUnsettledAppPaymentsCountAsync(app.Id);

        return appDto;
    }
}
