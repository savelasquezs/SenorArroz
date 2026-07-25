using MediatR;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Features.BusinessDocuments.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BusinessDocuments.Queries;

public sealed record GetBusinessDocumentsQuery(
    string? Search,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "name",
    string SortOrder = "asc") : IRequest<PagedResult<BusinessDocumentDto>>;

public sealed class GetBusinessDocumentsHandler
    : IRequestHandler<GetBusinessDocumentsQuery, PagedResult<BusinessDocumentDto>>
{
    private readonly IBusinessDocumentRepository _repository;
    private readonly BusinessDocumentDtoFactory _dtoFactory;

    public GetBusinessDocumentsHandler(
        IBusinessDocumentRepository repository,
        IOptions<ApiPublicOptions> apiPublicOptions)
    {
        _repository = repository;
        _dtoFactory = new BusinessDocumentDtoFactory(apiPublicOptions);
    }

    public async Task<PagedResult<BusinessDocumentDto>> Handle(
        GetBusinessDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var result = await _repository.GetPagedAsync(
            request.Search,
            page,
            pageSize,
            request.SortBy,
            request.SortOrder,
            cancellationToken);

        return new PagedResult<BusinessDocumentDto>
        {
            Items = result.Items.Select(_dtoFactory.Create).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
        };
    }
}
