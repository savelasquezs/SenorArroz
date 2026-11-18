using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class OrderBenefitEligibility
{
    public static async Task<(LoyaltyCycleStep? Step, int Delivered)> LoyaltyAsync(IApplicationDbContext db, int branchId,
        int? customerId, DateTime now, CancellationToken ct)
    {
        if (!customerId.HasValue) return (null, 0);
        var reserved = await db.Orders.AsNoTracking().AnyAsync(x => x.CustomerId == customerId
            && x.AppliedBenefitType == OrderBenefitType.Loyalty && x.Status != OrderStatus.Cancelled && x.Status != OrderStatus.Delivered, ct)
            || await db.StorefrontCheckouts.AsNoTracking().AnyAsync(x => x.CustomerId == customerId
                && x.AppliedBenefitType == OrderBenefitType.Loyalty && x.Status == "pending" && x.ExpiresAt > now, ct);
        if (reserved) return (null, 0);
        var delivered = await db.Orders.AsNoTracking().CountAsync(x => x.CustomerId == customerId && x.Status == OrderStatus.Delivered, ct);
        if (LoyaltyDeliveriesPerReward.GetDeliveriesUntilNextReward(delivered) != 1) return (null, delivered);
        var length = await db.LoyaltyCycleSteps.AsNoTracking().Where(x => x.BranchId == branchId && x.IsActive)
            .Select(x => (int?)x.StepIndex).MaxAsync(ct) ?? 0;
        if (length <= 0) return (null, delivered);
        var index = LoyaltyDeliveriesPerReward.GetStepIndexAtMilestone(delivered + 1, length);
        return (await db.LoyaltyCycleSteps.AsNoTracking().Include(x => x.GiftProduct)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.StepIndex == index && x.IsActive, ct), delivered);
    }

    public static async Task<(string? Error, string? Snapshot)> ValidateAsync(IApplicationDbContext db,
        CreateOrderDto order, string role, DateTime now, CancellationToken ct)
    {
        var subtotal = order.OrderDetails.Sum(x => (long)x.UnitPrice * x.Quantity);
        switch (order.AppliedBenefitType)
        {
            case null or OrderBenefitType.None: return (null, null);
            case OrderBenefitType.Manual:
                return Roles.IsAdminOrSuperadmin(role) ? (null, order.ManualBenefitReason)
                    : ("Solo administradores pueden conceder beneficios manuales.", null);
            case OrderBenefitType.DailyPromotion:
                var promotion = await db.DailyPromotions.AsNoTracking().Include(x => x.DiscountProducts)
                    .Where(x => x.BranchId == order.BranchId && x.IsActive && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now))
                    .OrderByDescending(x => x.StartsAt).FirstOrDefaultAsync(ct);
                if (promotion is null || promotion.Id != order.AppliedBenefitSourceId || subtotal < (promotion.MinimumOrderValue ?? 0))
                    return ("La promoción cambió o no aplica en destino. Vuelve a elegir el beneficio.", null);
                return (null, JsonSerializer.Serialize(new { promotion.Id, promotion.Type, promotion.GiftProductId,
                    promotion.DiscountPercentage, promotion.DiscountScope, promotion.MinimumOrderValue, promotion.UpdatedAt,
                    Products = promotion.DiscountProducts.Select(x => x.ProductId).OrderBy(x => x) }));
            case OrderBenefitType.DiscountCode:
                var code = await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.AppliedBenefitSourceId
                    && x.BranchId == order.BranchId && x.IsActive && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now), ct);
                if (code is null || !string.Equals(code.Code, order.AppliedBenefitCode, StringComparison.OrdinalIgnoreCase) || subtotal < (code.MinimumOrderValue ?? 0))
                    return ("El código ya no aplica en la sucursal que prepara. Valídalo nuevamente.", null);
                return (null, JsonSerializer.Serialize(new { code.Id, code.Code, code.Type, code.GiftProductId, code.DiscountPercentage, code.MinimumOrderValue, code.UpdatedAt }));
            case OrderBenefitType.Loyalty:
                var (step, delivered) = await LoyaltyAsync(db, order.BranchId, order.CustomerId, now, ct);
                if (step?.RewardType is null || step.Id != order.AppliedBenefitSourceId)
                    return ("El premio del cliente cambió o está reservado en otro pedido. Revisa la fidelización.", null);
                return (null, JsonSerializer.Serialize(new { step.Id, step.RewardType, step.GiftProductId, step.DiscountPercentage, step.UpdatedAt, delivered }));
            default: return ("Beneficio inválido.", null);
        }
    }
}
