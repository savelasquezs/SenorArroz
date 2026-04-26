using SenorArroz.Application.Common;
using SenorArroz.Application.Common.Helpers;
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
                Phone = JoinPhones(order.Customer?.Phone1, order.Customer?.Phone2),
                AddressDescription = order.Address?.AddressText,
                NeighborhoodName = order.Address?.Neighborhood?.Name,
                AddressAdditionalInfo = order.Address?.AdditionalInfo,
            };
        }

        var branch = order.Branch;
        var logoPath = NormalizeReceiptLogoPath(branch?.PrintSettings?.ReceiptLogoPath);

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
            BranchPhone = NullIfWhiteSpace(JoinPhones(branch?.Phone1, branch?.Phone2)),
            BranchAddress = branch?.Address,
            ReceiptLogoUrl = PublicUrlHelper.ToAbsolutePublicUrl(publicApiBaseUrl, logoPath),
            ReceiptLogoPath = logoPath,
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
                CashToCollect = OrderCashPortionHelper.GetCashToCollectDisplay(order),
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
            CreatedAt = order.CreatedAt,
            OrderNotes = NullIfWhiteSpace(order.Notes),
        };
    }

    /// <summary>Id sintético en <c>order_ids_json</c> y en el payload; no corresponde a un pedido real.</summary>
    public const int TestPrintSyntheticOrderId = 900001;

    /// <summary>Snapshot de prueba sin pedido en BD. <paramref name="branch"/> debe incluir <see cref="Branch.PrintSettings"/>.</summary>
    public static PrintTicketPayloadBatchV1 BuildTestBatch(
        Branch branch,
        PrintJobKind kind,
        DateTime printedAtUtc,
        string? publicApiBaseUrl,
        string restaurantDisplayName,
        string kitchenFooterMessage)
    {
        if (kind is not PrintJobKind.Kitchen and not PrintJobKind.Delivery)
            throw new ArgumentOutOfRangeException(nameof(kind));

        var kindStr = KindToApiString(kind);
        var logoPath = NormalizeReceiptLogoPath(branch.PrintSettings?.ReceiptLogoPath);

        var banner = new PrintTicketLineV1
        {
            ProductName = "════════ PRUEBA DE IMPRESIÓN ════════",
            KitchenProductName = "PRUEBA",
            Quantity = 1,
            UnitPrice = 0,
            LineSubtotal = 0,
            LineDiscount = 0,
            LineGrossSubtotal = 0,
            LineDiscountPercent = null,
            Notes = "No preparar ni entregar.",
        };

        const string line2Name = "Arroz con pollo y ensalada";
        var gross2 = 28000 * 2;
        var disc2 = 2000;
        int? pct2 = gross2 > 0 && disc2 > 0
            ? Math.Clamp((int)Math.Round(100.0 * disc2 / gross2), 1, 100)
            : null;

        var line2 = new PrintTicketLineV1
        {
            ProductName = line2Name,
            KitchenProductName = KitchenProductNameFormatter.Format(line2Name),
            Quantity = 2,
            UnitPrice = 28000,
            LineSubtotal = gross2 - disc2,
            LineDiscount = disc2,
            LineGrossSubtotal = gross2,
            LineDiscountPercent = pct2,
            Notes = "Sin cebolla",
        };

        const string line3Name = "Gaseosa Coca-Cola 400 ml";
        var gross3 = 3500;
        var line3 = new PrintTicketLineV1
        {
            ProductName = line3Name,
            KitchenProductName = KitchenProductNameFormatter.Format(line3Name),
            Quantity = 1,
            UnitPrice = 3500,
            LineSubtotal = gross3,
            LineDiscount = 0,
            LineGrossSubtotal = gross3,
            LineDiscountPercent = null,
            Notes = null,
        };

        var lines = new List<PrintTicketLineV1> { banner, line2, line3 };
        var subtotal = line2.LineSubtotal + line3.LineSubtotal;
        var discountTotal = line2.LineDiscount;
        var deliveryFee = kind == PrintJobKind.Delivery ? 4500 : 0;
        var grandTotal = subtotal + deliveryFee;

        PrintTicketCustomerV1? customer = kind == PrintJobKind.Delivery
            ? new PrintTicketCustomerV1
            {
                Name = "Cliente de prueba",
                Phone = "300 1234567",
                AddressDescription = "Calle 10 # 23-45",
                NeighborhoodName = "Centro",
                AddressAdditionalInfo = "Casa blanca, timbre 2",
            }
            : new PrintTicketCustomerV1
            {
                Name = "Cliente de prueba (mostrador)",
                Phone = "300 9876543",
            };

        var orderPayload = new PrintTicketOrderPayloadV1
        {
            OrderId = TestPrintSyntheticOrderId,
            BranchName = branch.Name ?? string.Empty,
            BusinessName = NullIfWhiteSpace(branch.BusinessName),
            BranchNit = NullIfWhiteSpace(branch.Nit),
            BranchPhone = NullIfWhiteSpace(JoinPhones(branch.Phone1, branch.Phone2)),
            BranchAddress = branch.Address,
            ReceiptLogoUrl = PublicUrlHelper.ToAbsolutePublicUrl(publicApiBaseUrl, logoPath),
            ReceiptLogoPath = logoPath,
            RestaurantDisplayName = NullIfWhiteSpace(restaurantDisplayName),
            KitchenFooterMessage = NullIfWhiteSpace(kitchenFooterMessage),
            Kind = kindStr,
            PrintedAtUtc = printedAtUtc,
            Lines = lines,
            Totals = new PrintTicketTotalsV1
            {
                Subtotal = subtotal,
                DiscountTotal = discountTotal,
                DeliveryFee = deliveryFee,
                GrandTotal = grandTotal,
                CashToCollect = kind == PrintJobKind.Delivery ? grandTotal : null,
            },
            Customer = customer,
            Payments = new PrintTicketPaymentsV1(),
            OrderType = kind == PrintJobKind.Delivery ? "delivery" : "onsite",
            OrderStatus = kind == PrintJobKind.Delivery ? "on_the_way" : "in_preparation",
            PrepareAt = printedAtUtc,
            CreatedAt = printedAtUtc,
            OrderNotes = kind == PrintJobKind.Delivery ? null : "Nota de prueba (pedido en local).",
        };

        return new PrintTicketPayloadBatchV1 { Version = 1, Orders = new List<PrintTicketOrderPayloadV1> { orderPayload } };
    }

    private static string KindToApiString(PrintJobKind k) => k switch
    {
        PrintJobKind.Kitchen => Roles.Kitchen,
        PrintJobKind.Delivery => "delivery",
        PrintJobKind.Cashier => Roles.Cashier,
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

    private static string? NormalizeReceiptLogoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var p = path.Trim();
        if (Uri.TryCreate(p, UriKind.Absolute, out var u) &&
            (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            return p;
        return p.StartsWith('/') ? p : "/" + p;
    }

    private static string? JoinPhones(string? phone1, string? phone2)
    {
        var a = NullIfWhiteSpace(phone1);
        var b = NullIfWhiteSpace(phone2);
        if (a is null) return b;
        if (b is null) return a;
        return $"{a} - {b}";
    }
}
