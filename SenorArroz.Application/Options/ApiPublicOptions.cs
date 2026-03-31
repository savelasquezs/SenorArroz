namespace SenorArroz.Application.Options;

/// <summary>URL pública del host de la API (sin /api), para armar enlaces absolutos a estáticos (logos de ticket).</summary>
public class ApiPublicOptions
{
    public const string SectionName = "ApiPublic";

    /// <summary>Ej: https://tu-api.up.railway.app — sin barra final. Vacío en dev si el front usa el mismo origen.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
