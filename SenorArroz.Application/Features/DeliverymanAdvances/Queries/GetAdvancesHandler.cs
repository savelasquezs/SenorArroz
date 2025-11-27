using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Queries;

public class GetAdvancesHandler : IRequestHandler<GetAdvancesQuery, PagedResult<DeliverymanAdvanceDto>>
{
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetAdvancesHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _advanceRepository = advanceRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<DeliverymanAdvanceDto>> Handle(GetAdvancesQuery request, CancellationToken cancellationToken)
    {
        // Determinar branch según rol
        int? branchId = request.BranchId;
        if (_currentUser.Role != "superadmin")
        {
            branchId = _currentUser.BranchId;
        }

        var pagedAdvances = await _advanceRepository.GetPagedAsync(
            deliverymanId: request.DeliverymanId,
            branchId: branchId,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            page: request.Page,
            pageSize: request.PageSize,
            sortBy: request.SortBy,
            sortOrder: request.SortOrder);

        var advanceDtos = pagedAdvances.Items.Select(a => _mapper.Map<DeliverymanAdvanceDto>(a)).ToList();

        return new PagedResult<DeliverymanAdvanceDto>
        {
            Items = advanceDtos,
            TotalCount = pagedAdvances.TotalCount,
            Page = pagedAdvances.Page,
            PageSize = pagedAdvances.PageSize,
            TotalPages = pagedAdvances.TotalPages
        };
    }
}

