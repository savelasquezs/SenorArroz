using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetDeliveryAdvanceOrdersHandler : IRequestHandler<GetDeliveryAdvanceOrdersQuery, List<DeliveryAdvanceOrderRowDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetDeliveryAdvanceOrdersHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<DeliveryAdvanceOrderRowDto>> Handle(GetDeliveryAdvanceOrdersQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                && o.Type == OrderType.Delivery
                && (o.Status == OrderStatus.Ready || o.Status == OrderStatus.OnTheWay))
            .OrderBy(o => o.Id)
            .Select(o => new DeliveryAdvanceOrderRowDto
            {
                Id = o.Id,
                Total = o.Total,
                Status = o.Status == OrderStatus.Ready ? "ready" : "on_the_way",
                AddressSummary = o.Address != null
                    ? o.Address.AddressText + (o.Address.AdditionalInfo != null ? " · " + o.Address.AdditionalInfo : "")
                    : "",
            })
            .ToListAsync(cancellationToken);

        return rows;
    }
}
