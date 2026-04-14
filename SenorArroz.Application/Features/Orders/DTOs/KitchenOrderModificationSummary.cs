namespace SenorArroz.Application.Features.Orders.DTOs;

public class KitchenOrderModificationSummary
{
    public List<KitchenOrderAddedLineDto> AddedLines { get; set; } = new();
    public List<KitchenOrderRemovedLineDto> RemovedLines { get; set; } = new();
    public List<KitchenOrderQuantityChangeDto> QuantityChanges { get; set; } = new();
    public List<KitchenOrderProductReplacementDto> ProductReplacements { get; set; } = new();
    public bool ScheduleChanged { get; set; }
    public bool NotesChanged { get; set; }
}

public class KitchenOrderAddedLineDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class KitchenOrderRemovedLineDto
{
    public string ProductName { get; set; } = string.Empty;
}

public class KitchenOrderQuantityChangeDto
{
    public string ProductName { get; set; } = string.Empty;
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
}

public class KitchenOrderProductReplacementDto
{
    public string PreviousProductName { get; set; } = string.Empty;
    public string NewProductName { get; set; } = string.Empty;
}
