using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Inventory.Helpers;

public static class InventoryCatalogHelper
{
    public static void ValidateConversions(bool tracksInventory, bool active, IReadOnlyCollection<InventoryConversionInput> conversions)
    {
        if (active && !tracksInventory) throw new BusinessException("Un insumo no puede activar inventario sin controlar existencias");
        if (!tracksInventory && conversions.Count > 0) throw new BusinessException("Las conversiones solo aplican a insumos inventariables");
        if (conversions.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.BaseQuantity <= 0))
            throw new BusinessException("Cada presentación debe tener nombre y una equivalencia mayor que cero");
        if (conversions.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new BusinessException("No pueden repetirse presentaciones del mismo insumo");
        if (tracksInventory && conversions.Count == 0)
            throw new BusinessException("Un insumo inventariable necesita al menos una presentación de compra");
    }

    public static void ReplaceConversions(int expenseId, IReadOnlyCollection<InventoryConversionInput> inputs, IApplicationDbContext db)
    {
        var existing = db.ExpenseUnitConversions.Where(x => x.ExpenseId == expenseId).ToArray();
        db.ExpenseUnitConversions.RemoveRange(existing.Where(x => !inputs.Any(i => i.Name.Trim().Equals(x.Name, StringComparison.OrdinalIgnoreCase))));
        foreach (var input in inputs)
        {
            var name = input.Name.Trim();
            var item = existing.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item is null) db.ExpenseUnitConversions.Add(new ExpenseUnitConversion { ExpenseId = expenseId, Name = name, BaseQuantity = input.BaseQuantity, Active = input.Active });
            else { item.Name = name; item.BaseQuantity = input.BaseQuantity; item.Active = input.Active; }
        }
    }
}
