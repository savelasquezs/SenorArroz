namespace SenorArroz.Application.Options;

/// <summary>Nombre de marca en tickets (no depende de la sucursal).</summary>
public class BrandingOptions
{
    public const string SectionName = "Branding";

    /// <summary>Nombre del restaurante en cabecera de comanda de cocina.</summary>
    public string RestaurantDisplayName { get; set; } = "El señor arroz";

    /// <summary>Última línea del ticket de cocina (sin emoji si la impresora no soporta UTF-8).</summary>
    public string KitchenFooterMessage { get; set; } = "Gracias por confiar en El señor arroz";
}
