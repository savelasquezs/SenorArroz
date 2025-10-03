using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class SearchOrdersQuery : IRequest<PagedResult<OrderDto>>
{
    public string? SearchTerm { get; set; }
    public int? BranchId { get; set; }
    public int? CustomerId { get; set; }
    public int? DeliveryManId { get; set; }
    public OrderStatus? Status { get; set; }
    public OrderType? Type { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; }
    public string SortOrder { get; set; } = "asc";
}
