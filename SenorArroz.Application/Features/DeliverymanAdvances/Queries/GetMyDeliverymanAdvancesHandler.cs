using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Queries;

public class GetMyDeliverymanAdvancesHandler
    : IRequestHandler<GetMyDeliverymanAdvancesQuery, List<DeliverymanAdvanceDto>>
{
    private const int PageSize = 2000;

    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetMyDeliverymanAdvancesHandler(
        IDeliverymanAdvanceRepository advanceRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _advanceRepository = advanceRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<List<DeliverymanAdvanceDto>> Handle(
        GetMyDeliverymanAdvancesQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_currentUser.Role, "deliveryman", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Solo domiciliarios pueden consultar sus abonos.");

        var paged = await _advanceRepository.GetPagedAsync(
            deliverymanId: _currentUser.Id,
            branchId: null,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            page: 1,
            pageSize: PageSize,
            sortBy: "createdAt",
            sortOrder: "desc");

        return paged.Items.Select(a => _mapper.Map<DeliverymanAdvanceDto>(a)).ToList();
    }
}
