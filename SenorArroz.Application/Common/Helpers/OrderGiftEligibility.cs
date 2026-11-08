using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class OrderGiftEligibility
{
    public static bool CanGrantManualBenefit(string role, LoyaltyRewardType? rewardType) =>
        Roles.IsAdminOrSuperadmin(role) || Roles.IsCashier(role) && rewardType == LoyaltyRewardType.GiftProduct;

    public static Task<bool> IsValidProductAsync(IApplicationDbContext db, int productId, CancellationToken ct) =>
        db.Products.AsNoTracking().AnyAsync(product => product.Id == productId && product.Active
            && product.Category.Name.Trim().ToLower() == "regalos", ct);
}
