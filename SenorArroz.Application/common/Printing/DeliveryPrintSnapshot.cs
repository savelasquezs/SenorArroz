using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models.Printing;

namespace SenorArroz.Application.Common.Printing;

public sealed class DeliveryPrintSnapshot
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public OrderStatus Status { get; set; }
    public OrderType? Type { get; set; }
    public int? DeliveryManId { get; set; }
    public string? GuestName { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone1 { get; set; }
    public string? CustomerPhone2 { get; set; }
    public string? AddressDescription { get; set; }
    public string? AddressAdditionalInfo { get; set; }
    public string? NeighborhoodName { get; set; }
    public int Subtotal { get; set; }
    public int DiscountTotal { get; set; }
    public int DeliveryFee { get; set; }
    public int Total { get; set; }
    public bool PaidInStoreCash { get; set; }
    public DateTime? ReservedFor { get; set; }
    public DateTime? PrepareAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
    public List<DeliveryPrintLineSnapshot> Lines { get; set; } = new();
    public List<DeliveryPrintBankPaymentSnapshot> BankPayments { get; set; } = new();
    public List<DeliveryPrintAppPaymentSnapshot> AppPayments { get; set; } = new();
}

public sealed class DeliveryPrintLineSnapshot
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int Discount { get; set; }
    public int? Subtotal { get; set; }
    public string? Notes { get; set; }
}

public sealed class DeliveryPrintBankPaymentSnapshot
{
    public string BankName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class DeliveryPrintAppPaymentSnapshot
{
    public string AppName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public static class DeliveryPrintPayloadBuilder
{
    public static PrintTicketPayloadBatchV1 BuildBatch(
        IReadOnlyList<DeliveryPrintSnapshot> orders,
        DateTime printedAtUtc)
    {
        return new PrintTicketPayloadBatchV1
        {
            Version = 1,
            Orders = orders
                .OrderBy(o => o.Id)
                .Select(o => BuildOrder(o, printedAtUtc))
                .ToList(),
        };
    }

    private static PrintTicketOrderPayloadV1 BuildOrder(
        DeliveryPrintSnapshot order,
        DateTime printedAtUtc)
    {
        var bankTotal = order.BankPayments.Sum(x => x.Amount);
        var appTotal = order.AppPayments.Sum(x => x.Amount);
        var cash = order.PaidInStoreCash
            ? 0
            : Math.Max(
                0,
                (int)Math.Round(
                    order.Total - bankTotal - appTotal,
                    MidpointRounding.AwayFromZero));

        var customerName = NullIfWhiteSpace(order.CustomerName)
            ?? NullIfWhiteSpace(order.GuestName);
        var phone = JoinPhones(order.CustomerPhone1, order.CustomerPhone2);
        var hasCustomer = customerName is not null
            || phone is not null
            || !string.IsNullOrWhiteSpace(order.AddressDescription);

        return new PrintTicketOrderPayloadV1
        {
            OrderId = order.Id,
            Kind = "delivery",
            PrintedAtUtc = printedAtUtc,
            OrderType = order.Type.HasValue ? OrderTypeToString(order.Type.Value) : null,
            OrderStatus = OrderStatusToString(order.Status),
            ReservedFor = order.ReservedFor,
            PrepareAt = order.PrepareAt,
            CreatedAt = order.CreatedAt,
            OrderNotes = NullIfWhiteSpace(order.Notes),
            OrderLevelNotes = NullIfWhiteSpace(order.Notes),
            Customer = hasCustomer
                ? new PrintTicketCustomerV1
                {
                    Name = customerName,
                    Phone = phone,
                    AddressDescription = NullIfWhiteSpace(order.AddressDescription),
                    AddressAdditionalInfo = NullIfWhiteSpace(order.AddressAdditionalInfo),
                    NeighborhoodName = NullIfWhiteSpace(order.NeighborhoodName),
                }
                : null,
            Lines = order.Lines
                .OrderBy(x => x.Id)
                .Select(x =>
                {
                    var gross = x.UnitPrice * x.Quantity;
                    return new PrintTicketLineV1
                    {
                        ProductName = x.ProductName,
                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        LineSubtotal = x.Subtotal ?? Math.Max(0, gross - x.Discount),
                        LineDiscount = x.Discount,
                        LineGrossSubtotal = gross,
                        LineDiscountPercent = gross > 0 && x.Discount > 0
                            ? Math.Clamp(
                                (int)Math.Round(100.0 * x.Discount / gross),
                                1,
                                100)
                            : null,
                        Notes = x.Notes,
                    };
                })
                .ToList(),
            Totals = new PrintTicketTotalsV1
            {
                Subtotal = order.Subtotal,
                DiscountTotal = order.DiscountTotal,
                DeliveryFee = order.DeliveryFee,
                GrandTotal = order.Total,
                CashToCollect = cash,
            },
            Payments = new PrintTicketPaymentsV1
            {
                Bank = order.BankPayments
                    .Select(x => new PrintTicketBankPaymentV1
                    {
                        BankName = x.BankName,
                        Amount = x.Amount,
                        IsVerified = x.IsVerified,
                    })
                    .ToList(),
                App = order.AppPayments
                    .Select(x => new PrintTicketAppPaymentV1
                    {
                        AppName = x.AppName,
                        Amount = x.Amount,
                    })
                    .ToList(),
            },
        };
    }

    private static string OrderTypeToString(OrderType value) => value switch
    {
        OrderType.Onsite => "onsite",
        OrderType.Delivery => "delivery",
        OrderType.Reservation => "reservation",
        _ => value.ToString().ToLowerInvariant(),
    };

    private static string OrderStatusToString(OrderStatus value) => value switch
    {
        OrderStatus.Taken => "taken",
        OrderStatus.InPreparation => "in_preparation",
        OrderStatus.Ready => "ready",
        OrderStatus.OnTheWay => "on_the_way",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => value.ToString().ToLowerInvariant(),
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? JoinPhones(string? phone1, string? phone2)
    {
        var first = NullIfWhiteSpace(phone1);
        var second = NullIfWhiteSpace(phone2);
        if (first is null) return second;
        if (second is null) return first;
        return $"{first} - {second}";
    }
}
