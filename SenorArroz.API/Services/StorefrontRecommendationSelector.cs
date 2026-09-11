using SenorArroz.API.Controllers;

namespace SenorArroz.API.Services;

internal static class StorefrontRecommendationSelector
{
    private const int CokeSmallId = 45;
    private const int CokeLargeId = 49;
    private const int FriesSmallId = 43;
    private const int FriesLargeId = 44;

    public static IReadOnlyCollection<StorefrontRecommendation> Select(
        PublicCatalogDto catalog,
        IReadOnlyCollection<WhatsAppCartItemState> cart,
        int limit)
    {
        var existing = cart.Select(x => x.ProductId).ToHashSet();
        var riceOptions = catalog.RiceGroups.SelectMany(x => x.Options).ToDictionary(x => x.ProductId);
        var comboOptions = catalog.ComboGroups.SelectMany(x => x.Options).ToDictionary(x => x.ProductId);
        var mainOptions = riceOptions.Concat(comboOptions).ToDictionary(x => x.Key, x => x.Value);
        var people = cart.Sum(item => mainOptions.TryGetValue(item.ProductId, out var option)
            ? (option.ServesPeopleMax ?? option.ServesPeopleMin ?? 1) * item.Quantity
            : 0);
        if (people == 0 || limit <= 0) return [];

        var beverages = catalog.BeverageGroups.SelectMany(x => x.Options).Select(x => x.ProductId).ToHashSet();
        var additions = catalog.AdditionGroups.SelectMany(x => x.Options).Select(x => x.ProductId).ToHashSet();
        var recommendations = new List<StorefrontRecommendation>(2);
        if (!existing.Overlaps(beverages))
            AddIfAvailable(recommendations, catalog.BeverageGroups, people >= 5 ? CokeLargeId : CokeSmallId);
        if (cart.Any(x => riceOptions.ContainsKey(x.ProductId)) && !existing.Overlaps(additions))
            AddIfAvailable(recommendations, catalog.AdditionGroups, people >= 5 ? FriesLargeId : FriesSmallId);
        return recommendations.Take(Math.Clamp(limit, 0, 3)).ToArray();
    }

    private static void AddIfAvailable(
        ICollection<StorefrontRecommendation> recommendations,
        IReadOnlyCollection<PublicProductGroupDto> groups,
        int productId)
    {
        var match = groups
            .SelectMany(group => group.Options.Select(option => (Group: group, Option: option)))
            .FirstOrDefault(x => x.Option.ProductId == productId && x.Option.AvailabilityStatus != "unavailable");
        if (match.Option is null) return;
        recommendations.Add(new StorefrontRecommendation(match.Group, match.Option));
    }
}

internal sealed record StorefrontRecommendation(
    PublicProductGroupDto Group,
    PublicProductOptionDto Option);
