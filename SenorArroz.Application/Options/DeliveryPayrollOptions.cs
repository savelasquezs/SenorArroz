namespace SenorArroz.Application.Options;

public class DeliveryPayrollOptions
{
    public const string SectionName = "DeliveryPayroll";

    /// <summary>Fracción del valor de delivery que se paga al domiciliario (ej. 0,70 = 70 %).</summary>
    public decimal DeliveryFeePayRate { get; set; } = 0.70m;
}
