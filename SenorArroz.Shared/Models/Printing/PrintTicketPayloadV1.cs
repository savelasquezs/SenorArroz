namespace SenorArroz.Shared.Models.Printing;

/// <summary>Raíz del snapshot en <c>payload_json</c> (versión 1). Serializar con nombres camelCase.</summary>
public class PrintTicketPayloadBatchV1
{
    public int Version { get; set; } = 1;

    public List<PrintTicketOrderPayloadV1> Orders { get; set; } = new();
}

public class PrintTicketOrderPayloadV1
{
    public int OrderId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    /// <summary>Nombre comercial en cabecera; si viene vacío el cliente de impresión puede usar <see cref="BranchName"/>.</summary>
    public string? BusinessName { get; set; }
    public string? BranchNit { get; set; }
    /// <summary>Teléfono(s) de contacto de la sucursal (cabecera comanda cocina / tickets).</summary>
    public string? BranchPhone { get; set; }
    public string? BranchAddress { get; set; }
    /// <summary>URL absoluta del logo (misma API pública que sirve estáticos).</summary>
    public string? ReceiptLogoUrl { get; set; }
    /// <summary>Ruta en el host (ej. /uploads/branch-print/1/logo.png). El agente la combina con su ApiBaseUrl si <see cref="ReceiptLogoUrl"/> es null.</summary>
    public string? ReceiptLogoPath { get; set; }

    /// <summary>Nombre de marca para cabecera de cocina (desde configuración).</summary>
    public string? RestaurantDisplayName { get; set; }
    /// <summary>Cierre del ticket de cocina.</summary>
    public string? KitchenFooterMessage { get; set; }

    /// <summary>Pedidos ya entregados del cliente (misma sucursal / programa).</summary>
    public int? LoyaltyDeliveredCount { get; set; }
    /// <summary>Pedidos faltantes para cerrar el ciclo de premios (null si no hay ciclo).</summary>
    public int? LoyaltyOrdersUntilCycleEnd { get; set; }
    public string? LoyaltyNextRewardLabel { get; set; }
    /// <summary>Regalo aplicado a este pedido, si ya fue asignado.</summary>
    public string? LoyaltyThisOrderGiftLabel { get; set; }

    /// <summary>valores: kitchen, delivery, cashier</summary>
    public string Kind { get; set; } = string.Empty;
    public DateTime PrintedAtUtc { get; set; }
    public List<PrintTicketLineV1> Lines { get; set; } = new();
    public PrintTicketTotalsV1 Totals { get; set; } = new();
    public PrintTicketCustomerV1? Customer { get; set; }
    public PrintTicketPaymentsV1 Payments { get; set; } = new();
    public string? LoyaltyRuleName { get; set; }
    public string? OrderType { get; set; }
    public string? OrderStatus { get; set; }
    public DateTime? ReservedFor { get; set; }
    public DateTime? PrepareAt { get; set; }
    /// <summary>Creación del pedido en UTC (fallback en comanda si no hay <see cref="PrepareAt"/>).</summary>
    public DateTime? CreatedAt { get; set; }
}

public class PrintTicketLineV1
{
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Nombre abreviado para cocina (arroz/con/chich). Si null, el agente usa <see cref="ProductName"/>.</summary>
    public string? KitchenProductName { get; set; }
    public int Quantity { get; set; }
    /// <summary>En pesos enteros (misma unidad que el dominio).</summary>
    public int UnitPrice { get; set; }
    public int LineSubtotal { get; set; }
    /// <summary>Descuento en pesos de la línea.</summary>
    public int LineDiscount { get; set; }
    /// <summary>Precio × cantidad antes de descuento.</summary>
    public int LineGrossSubtotal { get; set; }
    /// <summary>Porcentaje de descuento aproximado (1–100), null si no aplica.</summary>
    public int? LineDiscountPercent { get; set; }
    public string? Notes { get; set; }
}

public class PrintTicketTotalsV1
{
    public int Subtotal { get; set; }
    public int DiscountTotal { get; set; }
    public int DeliveryFee { get; set; }
    public int GrandTotal { get; set; }
    /// <summary>Efectivo a cobrar en domicilio (≥ 0). Null en snapshots viejos sin el campo.</summary>
    public int? CashToCollect { get; set; }
}

public class PrintTicketCustomerV1
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? AddressDescription { get; set; }
    public string? NeighborhoodName { get; set; }
    public string? AddressAdditionalInfo { get; set; }
}

public class PrintTicketPaymentsV1
{
    public List<PrintTicketBankPaymentV1> Bank { get; set; } = new();
    public List<PrintTicketAppPaymentV1> App { get; set; } = new();
}

public class PrintTicketBankPaymentV1
{
    public string BankName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsVerified { get; set; }
}

public class PrintTicketAppPaymentV1
{
    public string AppName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
