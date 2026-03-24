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

    /// <summary>Modo de liquidación del día (domiciliario).</summary>
    public enum DeliverymanDayLiquidationMode
    {
        None = 0,
        /// <summary>Liquidación total: tarjeta bloqueada hasta desbloqueo.</summary>
        FullLiquidation = 1,
        /// <summary>Liquidar y devolver base; sin bloqueo prolongado.</summary>
        LiquidateAndReturnBase = 2,
    }
}
