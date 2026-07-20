using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Branch : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre comercial en ticket (opcional). Si está vacío, se usa <see cref="Name"/>.</summary>
    public string? BusinessName { get; set; }

    /// <summary>NIT en ticket (opcional).</summary>
    public string? Nit { get; set; }

    public string Address { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>Tope COP del descuento “domicilio gratis” repartido en líneas del pedido (POS).</summary>
    public int MaxFreeDeliveryDiscount { get; set; } = 3000;

    /// <summary>Minutos mínimos de entrega/recogida en el mensaje copiable del POS.</summary>
    public int PosCopyEtaMinMinutes { get; set; } = 30;

    /// <summary>Minutos adicionales al mínimo para el tope de la ventana (p. ej. 30 + 15 → «30-45 min»).</summary>
    public int PosCopyEtaRangeMinutes { get; set; } = 15;

    /// <summary>Hora local de Colombia en la que deben cerrarse las jornadas abiertas.</summary>
    public TimeOnly DeliveryTrackingAutoCloseTime { get; set; } = new(21, 0);

    /// <summary>Frecuencia de captura sin pedidos en curso.</summary>
    public int DeliveryTrackingLightIntervalSeconds { get; set; } = 300;

    /// <summary>Frecuencia de captura durante un pedido en camino.</summary>
    public int DeliveryTrackingActiveIntervalSeconds { get; set; } = 30;

    /// <summary>Minutos mínimos para considerar una permanencia.</summary>
    public int DeliveryTrackingStayThresholdMinutes { get; set; } = 10;

    /// <summary>Radio máximo de los puntos que conforman una permanencia.</summary>
    public int DeliveryTrackingStayRadiusMeters { get; set; } = 50;

    /// <summary>Distancia tolerada respecto a la sucursal o al destino del pedido.</summary>
    public int DeliveryTrackingAllowedDistanceMeters { get; set; } = 50;

    /// <summary>Días de conservación de ubicaciones ordinarias.</summary>
    public int DeliveryTrackingLocationRetentionDays { get; set; } = 3;

    /// <summary>Días de conservación de incidentes y su evidencia.</summary>
    public int DeliveryTrackingIncidentRetentionDays { get; set; } = 15;

    public string? MenuImageUrl1 { get; set; }
    public string? MenuImageUrl2 { get; set; }

    // Navigation Properties
    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public virtual ICollection<Neighborhood> Neighborhoods { get; set; } = new List<Neighborhood>();
    public virtual ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
    public virtual ICollection<CommercialProfile> CommercialProfiles { get; set; } = new List<CommercialProfile>();
    public virtual ICollection<BranchBusinessHour> BusinessHours { get; set; } = new List<BranchBusinessHour>();
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    public virtual ICollection<Bank> Banks { get; set; } = new List<Bank>();
    public virtual ICollection<LoyaltyCycleStep> LoyaltyCycleSteps { get; set; } = new List<LoyaltyCycleStep>();
    public virtual ICollection<ExpenseHeader> ExpenseHeaders { get; set; } = new List<ExpenseHeader>();
    public virtual ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
    public virtual ICollection<CashRegisterClosure> CashRegisterClosures { get; set; } = new List<CashRegisterClosure>();
    public virtual ICollection<WhatsAppConversation> WhatsAppConversations { get; set; } = new List<WhatsAppConversation>();
    public virtual ICollection<WhatsAppQuickReply> WhatsAppQuickReplies { get; set; } = new List<WhatsAppQuickReply>();
    public virtual ICollection<WhatsAppTemplate> WhatsAppTemplates { get; set; } = new List<WhatsAppTemplate>();
    public virtual ICollection<DailyPromotion> DailyPromotions { get; set; } = new List<DailyPromotion>();
    public virtual ICollection<DiscountCode> DiscountCodes { get; set; } = new List<DiscountCode>();

    public virtual BranchPrintSettings? PrintSettings { get; set; }
    public virtual WhatsAppBranchSetting? WhatsAppSetting { get; set; }
    public virtual BranchAiSetting? AiSetting { get; set; }
}
