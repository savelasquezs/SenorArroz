using SenorArroz.API.Controllers;

namespace SenorArroz.API.Services;

internal static class StorefrontRecommendationSelector
{
    public static IReadOnlyCollection<StorefrontRecommendation> Select(
        PublicCatalogDto catalog,
        IReadOnlyCollection<WhatsAppCartItemState> cart,
        int limit)
    {
        var existing = cart.Select(x => x.ProductId).ToHashSet();
        var mainOptions = catalog.RiceGroups.Concat(catalog.ComboGroups).SelectMany(x => x.Options).ToDictionary(x => x.ProductId);
        var people = Math.Max(1, cart.Sum(item => mainOptions.TryGetValue(item.ProductId, out var option)
            ? (option.ServesPeopleMax ?? option.ServesPeopleMin ?? 1) * item.Quantity
            : 0));
        return new[]
        {
            (Priority: 0, Groups: catalog.BeverageGroups),
            (Priority: 1, Groups: catalog.AdditionGroups)
        }.SelectMany(entry => entry.Groups.SelectMany(group => group.Options
            .Where(option => option.AvailabilityStatus != "unavailable" && !existing.Contains(option.ProductId))
            .Select(option => new StorefrontRecommendation(
                group,
                option,
                entry.Priority,
                Math.Abs((option.ServesPeopleMax ?? option.ServesPeopleMin ?? 1) - people)))))
            .OrderBy(x => x.PeopleDistance)
            .ThenBy(x => x.RolePriority)
            .ThenBy(x => x.Group.SortOrder)
            .ThenBy(x => x.Option.Price)
            .Take(Math.Clamp(limit, 0, 3))
            .ToArray();
    }
}

internal sealed record StorefrontRecommendation(
    PublicProductGroupDto Group,
    PublicProductOptionDto Option,
    int RolePriority,
    int PeopleDistance);
