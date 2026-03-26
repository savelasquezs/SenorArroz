namespace SenorArroz.Application.Options;

public class DeliveryRouteOptions
{
    public const string SectionName = "DeliveryRoute";

    /// <summary>Segundos de espera tras la última asignación antes de consolidar la ruta (reloj operativo).</summary>
    public int ConsolidationDelaySeconds { get; set; } = 180;

    /// <summary>Margen por pedido sobre el tiempo de manejo Google (ej. 240 = 4 min).</summary>
    public int PerOrderBufferSeconds { get; set; } = 240;

    /// <summary>Segundos extra por parada si la dirección indica torre/bloque/unidad (ej. 300 = 5 min).</summary>
    public int ComplexAccessBufferSeconds { get; set; } = 300;

    /// <summary>Palabras separadas por coma (normalizadas sin tildes para búsqueda).</summary>
    public string ComplexAccessKeywords { get; set; } = "torre,bloque,unidad";
}
