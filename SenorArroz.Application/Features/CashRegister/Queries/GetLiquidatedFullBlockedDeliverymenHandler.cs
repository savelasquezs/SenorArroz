using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetLiquidatedFullBlockedDeliverymenHandler
    : IRequestHandler<GetLiquidatedFullBlockedDeliverymenQuery, List<LiquidatedDeliverymanOptionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetLiquidatedFullBlockedDeliverymenHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<LiquidatedDeliverymanOptionDto>> Handle(
        GetLiquidatedFullBlockedDeliverymenQuery request,
        CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var today = ColombiaTimeHelper.GetTodayDateOnlyColombia();

        return await _context.DeliverymanDayStates
            .AsNoTracking()
            .Where(s => s.BranchId == branchId
                && s.Date == today
                && s.Blocked
                && s.LiquidationMode == DeliverymanDayLiquidationMode.FullLiquidation)
            .OrderBy(s => s.Deliveryman.Name)
            .Select(s => new LiquidatedDeliverymanOptionDto
            {
                Id = s.DeliverymanId,
                Name = s.Deliveryman.Name,
            })
            .ToListAsync(cancellationToken);
    }
}
