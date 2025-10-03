using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrdersByStatusQuery : IRequest<PagedResult<OrderDto>>
{
    public OrderStatus Status { get; set; }
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; }
    public string SortOrder { get; set; } = "asc";
}
