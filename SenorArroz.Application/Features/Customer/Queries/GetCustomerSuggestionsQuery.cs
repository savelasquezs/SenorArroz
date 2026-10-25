using MediatR;

namespace SenorArroz.Application.Features.Customers.Queries;

public sealed class GetCustomerSuggestionsQuery : IRequest<CustomerSuggestionPageDto>
{
    public string Search { get; set; } = "";
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class CustomerSuggestionDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? WhatsAppUsername { get; set; }
    public List<string> BillingCompanies { get; set; } = [];
}

public sealed record CustomerSuggestionPageDto(IReadOnlyList<CustomerSuggestionDto> Items, int Page, bool HasMore);
