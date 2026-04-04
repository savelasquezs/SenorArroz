using System.Globalization;
using System.Linq;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Queries;

public class GetUserPayrollInsightsHandler : IRequestHandler<GetUserPayrollInsightsQuery, UserPayrollInsightsDto>
{
    private const int MaxRangeDays = 800;
    private const int OrderPageSize = 500;

    private readonly IUserRepository _userRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly DeliveryPayrollOptions _payrollOptions;

    public GetUserPayrollInsightsHandler(
        IUserRepository userRepository,
        IOrderRepository orderRepository,
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IOptions<DeliveryPayrollOptions> payrollOptions)
    {
        _userRepository = userRepository;
        _orderRepository = orderRepository;
        _db = db;
        _currentUser = currentUser;
        _payrollOptions = payrollOptions.Value;
    }

    public async Task<UserPayrollInsightsDto> Handle(GetUserPayrollInsightsQuery request, CancellationToken cancellationToken)
    {
        var granularity = NormalizeGranularity(request.SeriesGranularity);
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
            throw new BusinessException("Indique fechas desde y hasta (YYYY-MM-DD).");

        if (!DateOnly.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDay)
            || !DateOnly.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDay))
            throw new BusinessException("Fechas inválidas (use YYYY-MM-DD).");

        if (toDay < fromDay)
            (fromDay, toDay) = (toDay, fromDay);

        var span = toDay.DayNumber - fromDay.DayNumber + 1;
        if (span > MaxRangeDays)
            throw new BusinessException($"El rango no puede superar {MaxRangeDays} días.");

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
            throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");

        var branchId = _currentUser.Role == "superadmin"
            ? user.BranchId
            : _currentUser.BranchId;
        if (_currentUser.Role != "superadmin" && user.BranchId != branchId)
            throw new BusinessException("No tienes permisos para ver los datos de este usuario");

        var fromCol = fromDay.ToDateTime(TimeOnly.MinValue);
        var toCol = toDay.ToDateTime(TimeOnly.MinValue);
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(fromCol, toCol);

        var payRate = ClampPayRate(_payrollOptions.DeliveryFeePayRate);
        var isDm = user.Role == UserRole.Deliveryman;

        var details = await LoadExpenseDetailsAsync(user, fromUtc, toUtc, cancellationToken);
        var orders = isDm
            ? await LoadDeliveredOrdersAsync(user.Id, branchId, fromUtc, toUtc)
            : new List<Order>();

        var filteredOrders = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            orders, fromUtc, toUtc, lastLiquidationAtUtc: null, useSettlementCycle: false);

        var periodExpenseTotal = details.Sum(DetailLineTotal);
        var periodFeeSum = filteredOrders.Sum(o => o.DeliveryFee ?? 0);
        var periodPayable = Math.Round(periodFeeSum * payRate, 2);

        var culture = CultureInfo.GetCultureInfo("es-CO");
        var buckets = BuildBuckets(fromDay, toDay, granularity, culture);

        var series = buckets.Select(b =>
        {
            var (bf, bt) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(b.RangeFrom, b.RangeTo);
            var dIn = details.Where(x => x.Header.CreatedAt >= bf && x.Header.CreatedAt <= bt).ToList();
            var oIn = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
                filteredOrders, bf, bt, null, false);
            var fee = oIn.Sum(o => o.DeliveryFee ?? 0);
            return new UserPayrollSeriesPointDto
            {
                Key = b.Key,
                Label = b.Label,
                ExpenseLinesTotal = dIn.Sum(DetailLineTotal),
                DeliveredOrdersCount = oIn.Count,
                SumDeliveryFee = fee,
                PayableDeliveryFee = Math.Round(fee * payRate, 2),
            };
        }).ToList();

        return new UserPayrollInsightsDto
        {
            LinkedExpense = user.PayrollExpenseId.HasValue && user.PayrollExpense != null
                ? new UserPayrollLinkedExpenseDto { Id = user.PayrollExpense.Id, Name = user.PayrollExpense.Name }
                : null,
            DeliveryFeePayRate = payRate,
            FromDate = fromDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate = toDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SeriesGranularity = granularity,
            Period = new UserPayrollPeriodTotalsDto
            {
                ExpenseLinesTotal = periodExpenseTotal,
                ExpenseLines = details.Select(d => new UserPayrollExpenseLineItemDto
                {
                    DetailId = d.Id,
                    HeaderId = d.HeaderId,
                    HeaderCreatedAt = d.Header.CreatedAt,
                    LineTotal = DetailLineTotal(d),
                    Notes = d.Notes,
                }).ToList(),
                DeliveredOrdersCount = filteredOrders.Count,
                SumDeliveryFee = periodFeeSum,
                PayableDeliveryFee = periodPayable,
                IsDeliveryman = isDm,
            },
            Series = series,
        };
    }

    private static string NormalizeGranularity(string? g)
    {
        var x = (g ?? "day").Trim().ToLowerInvariant();
        return x switch
        {
            "day" or "month" or "biweek" => x,
            _ => throw new BusinessException("seriesGranularity debe ser day, month o biweek."),
        };
    }

    private static decimal ClampPayRate(decimal rate)
    {
        if (rate < 0) return 0;
        if (rate > 1) return 1;
        return rate;
    }

    private static decimal DetailLineTotal(ExpenseDetail d) =>
        d.Total ?? Math.Round((decimal)d.Amount * d.Quantity, 2);

    private async Task<List<ExpenseDetail>> LoadExpenseDetailsAsync(
        User user,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        if (!user.PayrollExpenseId.HasValue)
            return new List<ExpenseDetail>();

        return await _db.ExpenseDetails
            .AsNoTracking()
            .Include(d => d.Header)
            .Where(d => d.ExpenseId == user.PayrollExpenseId.Value
                        && d.Header.BranchId == user.BranchId
                        && d.Header.CreatedAt >= fromUtc
                        && d.Header.CreatedAt <= toUtc)
            .OrderByDescending(d => d.Header.CreatedAt)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Order>> LoadDeliveredOrdersAsync(
        int deliverymanId,
        int branchId,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var all = new List<Order>();
        var page = 1;
        while (page < 200)
        {
            var batch = await _orderRepository.SearchOrdersAsync(
                searchTerm: null,
                branchId: branchId,
                customerId: null,
                deliveryManId: deliverymanId,
                status: OrderStatus.Delivered,
                type: OrderType.Delivery,
                fromDate: fromUtc,
                toDate: toUtc,
                minAmount: null,
                maxAmount: null,
                page: page,
                pageSize: OrderPageSize,
                sortBy: "CreatedAt",
                sortOrder: "desc");

            all.AddRange(batch.Items);
            if (batch.Items.Count() < OrderPageSize)
                break;
            page++;
        }

        return all;
    }

    private sealed record BucketDef(string Key, string Label, DateTime RangeFrom, DateTime RangeTo);

    private static List<BucketDef> BuildBuckets(DateOnly fromDay, DateOnly toDay, string granularity, CultureInfo culture)
    {
        var list = new List<BucketDef>();
        if (granularity == "day")
        {
            for (var d = fromDay; d <= toDay; d = d.AddDays(1))
            {
                var dt = d.ToDateTime(TimeOnly.MinValue);
                list.Add(new BucketDef(
                    d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    dt.ToString("ddd d MMM yyyy", culture),
                    dt,
                    dt));
            }

            return list;
        }

        if (granularity == "month")
        {
            var cur = new DateOnly(fromDay.Year, fromDay.Month, 1);
            var endMonth = new DateOnly(toDay.Year, toDay.Month, 1);
            while (cur <= endMonth)
            {
                var last = new DateOnly(cur.Year, cur.Month, DateTime.DaysInMonth(cur.Year, cur.Month));
                var rangeFrom = cur > fromDay ? cur : fromDay;
                var rangeTo = last < toDay ? last : toDay;
                var rf = rangeFrom.ToDateTime(TimeOnly.MinValue);
                var rt = rangeTo.ToDateTime(TimeOnly.MinValue);
                list.Add(new BucketDef(
                    $"{cur.Year:0000}-{cur.Month:00}",
                    new DateTime(cur.Year, cur.Month, 1).ToString("MMMM yyyy", culture),
                    rf,
                    rt));
                cur = cur.AddMonths(1);
            }

            return list;
        }

        // biweek
        var day = fromDay;
        while (day <= toDay)
        {
            var dt = day.ToDateTime(TimeOnly.MinValue);
            var (bs, be) = BiweekRangeColombia(dt);
            var bsOnly = DateOnly.FromDateTime(bs);
            var beOnly = DateOnly.FromDateTime(be);
            var rangeFrom = bsOnly < fromDay ? fromDay : bsOnly;
            var rangeTo = beOnly > toDay ? toDay : beOnly;
            var rf = rangeFrom.ToDateTime(TimeOnly.MinValue);
            var rt = rangeTo.ToDateTime(TimeOnly.MinValue);
            var key = $"{rangeFrom:yyyy-MM-dd}_{rangeTo:yyyy-MM-dd}";
            var label = rangeFrom == rangeTo
                ? rangeFrom.ToDateTime(TimeOnly.MinValue).ToString("d MMM yyyy", culture)
                : $"{rf:dd MMM} – {rt:dd MMM yyyy}";

            list.Add(new BucketDef(key, label, rf, rt));
            day = beOnly.AddDays(1);
        }

        return list;
    }

    /// <summary>Quincena calendario Colombia: días 1–15 y 16–fin de mes.</summary>
    private static (DateTime Start, DateTime End) BiweekRangeColombia(DateTime colombiaDate)
    {
        var y = colombiaDate.Year;
        var m = colombiaDate.Month;
        var d = colombiaDate.Day;
        if (d <= 15)
            return (new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Unspecified),
                new DateTime(y, m, 15, 0, 0, 0, DateTimeKind.Unspecified));
        var last = DateTime.DaysInMonth(y, m);
        return (new DateTime(y, m, 16, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(y, m, last, 0, 0, 0, DateTimeKind.Unspecified));
    }
}
