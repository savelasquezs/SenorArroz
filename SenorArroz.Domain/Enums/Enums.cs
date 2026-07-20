namespace SenorArroz.Domain.Enums
{
    public enum ExpenseUnit
    {
        Unit,
        Kilo,
        Package,
        Pound,
        Gallon
    }

    /// <summary>Destino de imputación de un gasto de catálogo hacia el menú (costo por gramo vendido).</summary>
    public enum ExpenseMenuTargetType
    {
        ProductCategory = 0,
        Product = 1,
    }

    public enum OrderStatus
    {
        Taken,          // taken
        InPreparation,  // in_preparation
        Ready,          // ready
        OnTheWay,       // on_the_way
        Delivered,      // delivered
        Cancelled       // cancelled
    }

    public enum OrderType
    {
        Onsite,       // onsite
        Delivery,     // delivery
        Reservation   // reservation
    }

    public enum UserRole
    {
        Superadmin,    // superadmin
        Admin,         // admin
        Cashier,       // cashier
        Kitchen,       // kitchen
        Deliveryman    // deliveryman
    }

    public enum ExternalOrderStatus
    {
        New,
        BlockedMapping,
        PendingAcceptance,
        Processing,
        Accepted,
        Rejected,
        Cancelled,
        SyncError
    }

    public enum DailyPromotionType
    {
        GiftProduct,
        FreeDelivery,
        PercentageDiscount
    }

    public enum DailyPromotionDiscountScope
    {
        AllProducts,
        SpecificProducts
    }

    public enum LoyaltyRewardType
    {
        GiftProduct,
        FreeDelivery,
        PercentageDiscount
    }

    public enum OrderBenefitType
    {
        None,
        DailyPromotion,
        Loyalty,
        DiscountCode,
        Manual
    }

    /// <summary>Bucket para la serie de peso vendido por categoría (dashboard ventas).</summary>
    public enum CategoryWeightEvolutionGranularity
    {
        Day,
        Month,
        Year,
    }

    public enum BankType
    {
        Normal,      // Bancos y apps operativos (Nequi, Bancolombia, Rappi, etc.)
        CashVault,   // Caja mayor efectivo (hidden)
        RealVault    // Caja mayor banco real (hidden)
    }

    /// <summary>Movimiento registrado desde efectivo físico hacia/desde el banco tipo Caja Mayor Efectivo (sin transferencia interbancaria).</summary>
    public enum CashVaultMovementKind
    {
        /// <summary>Efectivo que sale del cajón hacia la “caja mayor” representada en el sistema.</summary>
        AbonoToVault = 0,
        /// <summary>Efectivo que vuelve desde la caja mayor al cajón.</summary>
        WithdrawFromVault = 1,
    }

    /// <summary>Abono de domiciliario: cómo impacta caja / banco en el cuadre.</summary>
    public enum DeliverymanAdvancePaymentMethod
    {
        /// <summary>Efectivo entregado en caja (resta del efectivo esperado en cuadre).</summary>
        Cash = 0,
        /// <summary>Transferencia a cuenta de la sucursal (suma al banco en cuadre).</summary>
        BankTransfer = 1,
        /// <summary>Descuento por gasto asociado; no mueve efectivo ni banco en el cuadre.</summary>
        ExpenseOffset = 2,
    }

    /// <summary>Estado del ciclo ruta domicilio (métricas y SLA).</summary>
    public enum DeliveryRouteStatus
    {
        /// <summary>Aún se agregan pedidos; sin plan Google definitivo.</summary>
        Open,
        /// <summary>Plan consolidado; corre el reloj operativo vs meta.</summary>
        InProgress,
        /// <summary>Todos los pedidos entregados o cancelados.</summary>
        Completed,
        /// <summary>Ruta vaciada / abandonada sin completar (poco uso).</summary>
        Cancelled,
    }

    /// <summary>Modo de liquidación del día (domiciliario).</summary>
    public enum DeliverymanDayLiquidationMode
    {
        None = 0,
        /// <summary>Liquidación total: tarjeta bloqueada hasta desbloqueo.</summary>
        FullLiquidation = 1,
        /// <summary>Liquidar y devolver base; sin bloqueo prolongado.</summary>
        LiquidateAndReturnBase = 2,
    }

    /// <summary>Rol de comanda / cola de impresión térmica (agente local).</summary>
    public enum PrintJobKind
    {
        Kitchen,
        Delivery,
        Cashier,
    }

    public enum PrintJobStatus
    {
        Pending,
        Processing,
        Done,
        Failed,
    }

    public enum DeliveryWorkSessionStatus
    {
        Active,
        Closed,
    }

    public enum DeliveryWorkSessionEndReason
    {
        TotalSettlement,
        AutomaticClosure,
        AdministrativeClosure,
        UserChange,
        ExceptionalClosure,
    }

    public enum DeliveryTrackingMode
    {
        Light,
        ActiveDelivery,
        Offline,
        Stopped,
    }

    public enum DeliveryDeviceEventType
    {
        TrackingStarted,
        TrackingStopped,
        GpsDisabled,
        GpsEnabled,
        LocationPermissionRevoked,
        LocationPermissionRecovered,
        InternetLost,
        InternetRecovered,
        AppStopped,
        LocationServiceRestarted,
        BatteryLow,
        AutomaticClosure,
        TotalSettlement,
    }

    public enum DeliveryStayClassification
    {
        Branch,
        OrderDestination,
        AuthorizedPlace,
        TrafficOrRoute,
        UnexpectedPlace,
        GpsUnreliable,
        PendingReview,
    }

    public enum DeliveryTrackingIncidentType
    {
        Stay,
        RouteDeviation,
    }

    public enum DeliveryIncidentReviewStatus
    {
        Pending,
        Justified,
        NotJustified,
        GpsError,
        TechnicalFailure,
        ClosedWithoutAction,
        ReferredToDisciplinaryProcess,
    }

    public enum DeliveryTrackingAlertType
    {
        GpsDisabled,
        LocationPermissionRevoked,
        NoCommunication,
        UnexpectedStay,
        OfflineLocationsQueued,
        SessionPastAutoClose,
    }

    public enum DeliveryTrackingAlertSeverity
    {
        Informational,
        Warning,
        RequiresReview,
        Critical,
    }

    public enum DeliveryTrackingAlertStatus
    {
        Active,
        Resolved,
    }

    public enum WhatsAppConversationStatus
    {
        Open,
        Pending,
        Closed,
        Archived,
    }

    public enum WhatsAppMessageDirection
    {
        Inbound,
        Outbound,
    }

    public enum WhatsAppMessageType
    {
        Text,
        Image,
        Audio,
        Video,
        Document,
        Sticker,
    }

    public enum WhatsAppMessageStatus
    {
        Received,
        Sent,
        Delivered,
        Read,
        Failed,
    }
}
