namespace SenorArroz.Application.Features.Orders.DTOs;

public class SelfAssignOrdersDto
{
    public List<int> OrderIds { get; set; } = new();
    public string Password { get; set; } = string.Empty;
}
