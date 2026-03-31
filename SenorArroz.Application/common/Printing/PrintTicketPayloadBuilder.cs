using SenorArroz.Application.Common;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models.Printing;

namespace SenorArroz.Application.Common.Printing;

public static class PrintTicketPayloadBuilder
{
    public static PrintTicketPayloadBatchV1 BuildBatch(
        IReadOnlyList<Order> orders,
        PrintJobKind kind,
        DateTime printedAtUtc,
        string? publicApiBaseUrl,
        string restaurantDisplayName,
        string kitchenFooterMessage,
        IReadOnlyDictionary<int, LoyaltyKitchenSnapshot?> loyaltyByOrderId)
    {
        var batch = new PrintTicketPayloadBatchV1 { Version = 1 };
        foreach (var order in orders.OrderBy(o => o.Id))
        {
            loyaltyByOrderId.TryGetValue(order.Id, out var loyalty);
            batch.Orders.Add(BuildOrder(
                order,
                kind,
                printedAtUtc,
                publicApiBaseUrl,
                restaurantDisplayName,
                kitchenFooterMessage,
                loyalty));
        }

        return batch;
    }

    public static PrintTicketOrderPayloadV1 BuildOrder(
        Order order,
        PrintJobKind kind,
        DateTime printedAtUtc,
        string? publicApiBaseUrl,
        string restaurantDisplayName,
        string kitchenFooterMessage,
        LoyaltyKitchenSnapshot? loyalty)
    {
        var kindStr = KindToApiString(kind);
        var lines = order.OrderDetails
            .OrderBy(d => d.Id)
            .Select(d =>
            {
                var gross = d.UnitPrice * d.Quantity;
                var disc = d.Discount;
                int? pct = gross > 0 && disc > 0
                    ? Math.Clamp((int)Math.Round(100.0 * disc / gross), 1, 100)
                    : null;

                return new PrintTicketLineV1
                {
                    ProductName = d.Product?.Name ?? $"#{d.ProductId}",
                    KitchenProductName = KitchenProductNameFormatter.Format(d.Product?.Name ?? string.Empty),
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    LineSubtotal = d.Subtotal ?? Math.Max(0, gross - disc),
                    LineDiscount = disc,
                    LineGrossSubtotal = gross,
                    LineDiscountPercent = pct,
                    Notes = d.Notes,
                };
            })
            .ToList();

        var payments = new PrintTicketPaymentsV1
        {
            Bank = order.BankPayments
                .Select(b => new PrintTicketBankPaymentV1
                {
                    BankName = b.Bank?.Name ?? $"Banco #{b.BankId}",
                    Amount = b.Amount,
                    IsVerified = b.IsVerified,
                })
                .ToList(),
            App = order.AppPayments
                .Select(a => new PrintTicketAppPaymentV1
                {
                    AppName = a.App?.Name ?? $"App #{a.AppId}",
                    Amount = a.Amount,
                })
                .ToList(),
        };

        PrintTicketCustomerV1? customer = null;
        if (order.Customer != null || order.Address != null || !string.IsNullOrEmpty(order.GuestName))
        {
            customer = new PrintTicketCustomerV1
            {
                Name = order.Customer?.Name ?? order.GuestName,
                Phone = order.Customer?.Phone1 ?? order.Customer?.Phone2,
                AddressDescription = order.Address?.AddressText,
                NeighborhoodName = order.Address?.Neighborhood?.Name,
                AddressAdditionalInfo = order.Address?.AdditionalInfo,
            };
        }

        var branch = order.Branch;
        var logoPath = branch?.PrintSettings?.ReceiptLogoPath;

        int? loyDelivered = null;
        int? loyUntil = null;
        string? loyNext = null;
        string? loyGift = null;
        if (loyalty is not null)
        {
            loyDelivered = loyalty.DeliveredCount;
            loyUntil = loyalty.OrdersUntilCycleEnd;
            loyNext = NullIfWhiteSpace(loyalty.NextRewardLabel);
            loyGift = NullIfWhiteSpace(loyalty.ThisOrderGiftLabel);
        }

        return new PrintTicketOrderPayloadV1
        {
            OrderId = order.Id,
            BranchName = branch?.Name ?? string.Empty,
            BusinessName = NullIfWhiteSpace(branch?.BusinessName),
            BranchNit = NullIfWhiteSpace(branch?.Nit),
            BranchAddress = branch?.Address,
            ReceiptLogoUrl = PublicUrlHelper.ToAbsolutePublicUrl(publicApiBaseUrl, logoPath),
            RestaurantDisplayName = NullIfWhiteSpace(restaurantDisplayName),
            KitchenFooterMessage = NullIfWhiteSpace(kitchenFooterMessage),
            LoyaltyDeliveredCount = loyDelivered,
            LoyaltyOrdersUntilCycleEnd = loyUntil,
            LoyaltyNextRewardLabel = loyNext,
            LoyaltyThisOrderGiftLabel = loyGift,
            Kind = kindStr,
            PrintedAtUtc = printedAtUtc,
            Lines = lines,
            Totals = new PrintTicketTotalsV1
            {
                Subtotal = order.Subtotal,
                DiscountTotal = order.DiscountTotal,
                DeliveryFee = order.DeliveryFee ?? 0,
                GrandTotal = order.Total,
            },
            Customer = customer,
            Payments = payments,
            LoyaltyRuleName = !string.IsNullOrWhiteSpace(order.LoyaltyRewardSnapshot)
                ? order.LoyaltyRewardSnapshot
                : order.LoyaltyCycleStep?.RewardLabel,
            OrderType = order.Type.HasValue ? OrderTypeToString(order.Type.Value) : null,
            OrderStatus = OrderStatusToString(order.Status),
            ReservedFor = order.ReservedFor,
            PrepareAt = order.PrepareAt,
        };
    }

    private static string KindToApiString(PrintJobKind k) => k switch
    {
        PrintJobKind.Kitchen => "kitchen",
        PrintJobKind.Delivery => "delivery",
        PrintJobKind.Cashier => "cashier",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    private static string OrderTypeToString(OrderType t) => t switch
    {
        OrderType.Onsite => "onsite",
        OrderType.Delivery => "delivery",
        OrderType.Reservation => "reservation",
        _ => t.ToString().ToLowerInvariant(),
    };

    private static string OrderStatusToString(OrderStatus s) => s switch
    {
        OrderStatus.Taken => "taken",
        OrderStatus.InPreparation => "in_preparation",
        OrderStatus.Ready => "ready",
        OrderStatus.OnTheWay => "on_the_way",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => s.ToString().ToLowerInvariant(),
    };

    private static string? NullIfWhiteSpace(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
