namespace SenorArroz.Application.Features.BranchPrintSettings.DTOs;

/// <summary>Respuesta para el agente local (sin JWT).</summary>
public class PrintAgentConfigDto
{
    public string? PrinterQueueKitchen { get; set; }
    public string? PrinterQueueDelivery { get; set; }
    public string? PrinterQueueCashier { get; set; }

    public bool EnableKitchenJobs { get; set; }
    public bool EnableDeliveryJobs { get; set; }
    public bool EnableCashierJobs { get; set; }

    public short PaperWidthMm { get; set; }
}
