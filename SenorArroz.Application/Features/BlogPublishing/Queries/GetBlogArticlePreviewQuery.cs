using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BlogPublishing.DTOs;

namespace SenorArroz.Application.Features.BlogPublishing.Queries;

public sealed record GetBlogArticlePreviewQuery(string NotionPageId) : IRequest<BlogArticlePreviewDto>;

public sealed class GetBlogArticlePreviewHandler
    : IRequestHandler<GetBlogArticlePreviewQuery, BlogArticlePreviewDto>
{
    private readonly INotionBlogClient _notionBlogClient;

    public GetBlogArticlePreviewHandler(INotionBlogClient notionBlogClient)
    {
        _notionBlogClient = notionBlogClient;
    }

    public Task<BlogArticlePreviewDto> Handle(
        GetBlogArticlePreviewQuery request,
        CancellationToken cancellationToken) =>
        _notionBlogClient.GetPreviewAsync(request.NotionPageId, cancellationToken);
}
