using Microsoft.EntityFrameworkCore;using SenorArroz.Application.Common.Interfaces;using SenorArroz.Domain.Entities;using SenorArroz.Domain.Enums;
namespace SenorArroz.Application.Common.Services;
public class WhatsAppOrderDraftCalculator(IApplicationDbContext db):IWhatsAppOrderDraftCalculator
{
 public async Task<WhatsAppOrderDraft> Recalculate(int id,CancellationToken ct=default)
 {
  var d=await db.WhatsAppOrderDrafts.Include(x=>x.Items).ThenInclude(x=>x.Product).Include(x=>x.Address).ThenInclude(x=>x!.Neighborhood).FirstAsync(x=>x.Id==id,ct);EnsureEditable(d);
  foreach(var item in d.Items){item.UnitPrice=item.Product.Price;item.Subtotal=checked(item.UnitPrice*item.Quantity);}
  d.Subtotal=d.Items.Sum(x=>x.Subtotal);var covered=d.OrderType!=OrderType.Delivery||(d.Address?.Neighborhood.Active==true&&d.Address.Neighborhood.BranchId==d.BranchId);d.DeliveryFee=d.OrderType==OrderType.Delivery&&covered?d.Address!.Neighborhood.DeliveryFee:0;d.DiscountTotal=0;d.Total=Math.Max(0,d.Subtotal+d.DeliveryFee-d.DiscountTotal);d.ChangeAmount=d.PaymentMethod=="cash"&&d.CashReceived>=d.Total?d.CashReceived-d.Total:null;
  var available=d.Items.All(x=>x.Product.Active&&(!x.Product.Stock.HasValue||x.Product.Stock>=x.Quantity));
  d.Status=d.CustomerId is null?WhatsAppOrderDraftStatus.AwaitingCustomerData:d.Items.Count==0||d.OrderType is null?WhatsAppOrderDraftStatus.Building:d.OrderType==OrderType.Delivery&&(!d.AddressId.HasValue||!covered)?WhatsAppOrderDraftStatus.AwaitingAddress:string.IsNullOrWhiteSpace(d.PaymentMethod)?WhatsAppOrderDraftStatus.AwaitingPayment:available?WhatsAppOrderDraftStatus.Building:WhatsAppOrderDraftStatus.Building;
  d.Version++;await db.SaveChangesAsync(ct);return d;
 }
 public static void EnsureEditable(WhatsAppOrderDraft d){if(d.Status is WhatsAppOrderDraftStatus.Cancelled or WhatsAppOrderDraftStatus.Confirmed or WhatsAppOrderDraftStatus.ConvertedToOrder or WhatsAppOrderDraftStatus.Expired)throw new InvalidOperationException("El borrador ya no se puede modificar.");}
}
