using SenorArroz.Application.Common.Printing;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class PrintTicketPayloadBuilderTests
{
    [Fact]
    public void BuildOrder_maps_order_notes_to_OrderNotes()
    {
        var order = new Order
        {
            Id = 1,
            BranchId = 1,
            TakenById = 1,
            Status = OrderStatus.InPreparation,
            Type = OrderType.Onsite,
            Notes = "  Sin cebolla en el arroz  ",
            Subtotal = 20000,
            Total = 20000,
            CreatedAt = DateTime.UtcNow,
            Branch = new Branch { Id = 1, Name = "Prueba" },
            OrderDetails = new List<OrderDetail>(),
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        var payload = PrintTicketPayloadBuilder.BuildOrder(
            order,
            PrintJobKind.Kitchen,
            DateTime.UtcNow,
            null,
            "Marca",
            "Pie",
            loyalty: null);

        Assert.Equal("Sin cebolla en el arroz", payload.OrderNotes);
    }

    [Fact]
    public void BuildOrder_OrderNotes_is_null_when_notes_blank()
    {
        var order = new Order
        {
            Id = 2,
            BranchId = 1,
            TakenById = 1,
            Status = OrderStatus.Taken,
            Type = OrderType.Delivery,
            Notes = "   ",
            Subtotal = 0,
            Total = 0,
            CreatedAt = DateTime.UtcNow,
            Branch = new Branch { Id = 1, Name = "P" },
            OrderDetails = new List<OrderDetail>(),
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        var payload = PrintTicketPayloadBuilder.BuildOrder(
            order,
            PrintJobKind.Kitchen,
            DateTime.UtcNow,
            null,
            "M",
            "F",
            null);

        Assert.Null(payload.OrderNotes);
    }
}
