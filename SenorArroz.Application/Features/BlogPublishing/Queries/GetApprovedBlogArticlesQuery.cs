using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BlogPublishing.DTOs;

namespace SenorArroz.Application.Features.BlogPublishing.Queries;

public sealed record GetApprovedBlogArticlesQuery : IRequest<IReadOnlyList<BlogArticleSummaryDto>>;

public sealed class GetApprovedBlogArticlesHandler
    : IRequestHandler<GetApprovedBlogArticlesQuery, IReadOnlyList<BlogArticleSummaryDto>>
{
    private readonly INotionBlogClient _notionBlogClient;

    public GetApprovedBlogArticlesHandler(INotionBlogClient notionBlogClient)
    {
        _notionBlogClient = notionBlogClient;
    }

    public Task<IReadOnlyList<BlogArticleSummaryDto>> Handle(
        GetApprovedBlogArticlesQuery request,
        CancellationToken cancellationToken) =>
        _notionBlogClient.GetApprovedArticlesAsync(cancellationToken);
}
