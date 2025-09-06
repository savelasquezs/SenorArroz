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
}
