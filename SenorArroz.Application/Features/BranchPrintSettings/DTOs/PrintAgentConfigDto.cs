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

    /// <summary>Legado (una sola impresora); si el agente antiguo solo lee esto, sigue funcionando.</summary>
    public short PaperWidthMm { get; set; }

    public short PaperWidthMmKitchen { get; set; }
    public short PaperWidthMmDelivery { get; set; }
    public short PaperWidthMmCashier { get; set; }

    /// <summary>URL absoluta del logo para tickets; el agente y el payload pueden usarla para descargar la imagen.</summary>
    public string? ReceiptLogoUrl { get; set; }
}
