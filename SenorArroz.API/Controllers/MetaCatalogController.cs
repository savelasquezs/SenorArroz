using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.API.Controllers;

/// <summary>
/// Public product feed consumed by Meta Commerce Manager.
/// PostgreSQL remains the source of truth; Meta periodically downloads this CSV.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/meta")]
public sealed class MetaCatalogController(IApplicationDbContext db) : ControllerBase
{
    private const string Brand = "Señor Arroz";
    private const string StorefrontUrl = "https://senorarroz.com/menu";
    private const string DefaultImageUrl = "https://senorarroz.com/logo.png";

    private static readonly HashSet<string> PublicRoles =
        ["rice", "combo", "beverage", "addition"];

    private static readonly string[] Headers =
    [
        "id",
        "title",
        "description",
        "availability",
        "condition",
        "price",
        "link",
        "image_link",
        "brand",
        "item_group_id",
        "size",
        "product_tags[0]",
        "product_tags[1]"
    ];

    /// <summary>
    /// Returns a UTF-8 CSV compatible with Meta Commerce Manager scheduled data feeds.
    /// </summary>
    [HttpGet("catalog-feed.csv")]
    [Produces("text/csv")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetCatalogFeed(CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.CommercialProfile)
            .Where(x => x.Active && PublicRoles.Contains(x.Category.StorefrontRole))
            .OrderBy(x => x.Category.StorefrontRole)
            .ThenBy(x => x.CommercialProfileId)
            .ThenBy(x => x.StorefrontSortOrder)
            .ThenBy(x => x.Price)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var csv = BuildCsv(products);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8");
    }

    internal static string BuildCsv(IReadOnlyCollection<Product> products)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Headers);

        foreach (var product in products)
        {
            var role = product.Category.StorefrontRole;
            if (!product.Active || !PublicRoles.Contains(role))
                continue;

            var profile = product.CommercialProfile;
            var description = BuildDescription(product);
            var imageUrl = ResolveImageUrl(profile?.PhotoUrl);
            var groupId = product.CommercialProfileId.HasValue
                ? $"commercial_profile_{product.CommercialProfileId.Value}"
                : string.Empty;

            AppendRow(csv,
            [
                product.Id.ToString(CultureInfo.InvariantCulture),
                Limit(product.Name, 200),
                Limit(description, 9_999),
                product.Stock.HasValue && product.Stock.Value <= 0 ? "out of stock" : "in stock",
                "new",
                $"{product.Price.ToString(CultureInfo.InvariantCulture)} COP",
                StorefrontUrl,
                imageUrl,
                Brand,
                groupId,
                Limit(product.StorefrontVariantLabel ?? string.Empty, 200),
                role,
                Limit(product.Category.Name, 110)
            ]);
        }

        return csv.ToString();
    }

    private static string BuildDescription(Product product)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.CommercialProfile?.Description))
            parts.Add(SingleLine(product.CommercialProfile.Description));
        if (!string.IsNullOrWhiteSpace(product.CommercialProfile?.Ingredients))
            parts.Add($"Ingredientes: {SingleLine(product.CommercialProfile.Ingredients)}");

        if (product.ServesPeopleMin.HasValue || product.ServesPeopleMax.HasValue)
        {
            var serves = (product.ServesPeopleMin, product.ServesPeopleMax) switch
            {
                (int min, int max) when min == max => $"Rinde para {min} persona{(min == 1 ? string.Empty : "s")}.",
                (int min, int max) => $"Rinde para {min} a {max} personas.",
                (int min, null) => $"Rinde desde {min} persona{(min == 1 ? string.Empty : "s")}.",
                (null, int max) => $"Rinde hasta {max} personas.",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(serves))
                parts.Add(serves);
        }

        return parts.Count > 0 ? string.Join(" ", parts) : product.Name;
    }

    private static string ResolveImageUrl(string? photoUrl)
    {
        if (Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return uri.ToString();

        return DefaultImageUrl;
    }

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static void AppendRow(StringBuilder csv, IEnumerable<string> values)
    {
        csv.AppendLine(string.Join(',', values.Select(Csv)));
    }

    private static string Csv(string? value)
    {
        var normalized = value ?? string.Empty;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }
}
