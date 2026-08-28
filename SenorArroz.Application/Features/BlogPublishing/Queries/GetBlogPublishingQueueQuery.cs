using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BlogPublishing.Queries;

public sealed record GetBlogPublishingQueueQuery(int Page = 1, int PageSize = 30)
    : IRequest<PagedResult<BlogPublishingQueueItemDto>>;

public sealed class GetBlogPublishingQueueHandler
    : IRequestHandler<GetBlogPublishingQueueQuery, PagedResult<BlogPublishingQueueItemDto>>
{
    private readonly INotionBlogClient _notionBlogClient;
    private readonly IBlogPostRepository _repository;
    private readonly IBlogPublishingConfiguration _configuration;

    public GetBlogPublishingQueueHandler(
        INotionBlogClient notionBlogClient,
        IBlogPostRepository repository,
        IBlogPublishingConfiguration configuration)
    {
        _notionBlogClient = notionBlogClient;
        _repository = repository;
        _configuration = configuration;
    }

    public async Task<PagedResult<BlogPublishingQueueItemDto>> Handle(
        GetBlogPublishingQueueQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var approved = await _notionBlogClient.GetApprovedArticlesAsync(cancellationToken);
        var published = await _repository.GetPublishedAsync(cancellationToken);
        var result = new List<BlogPublishingQueueItemDto>(approved.Count + published.Count);
        var publishedIds = published
            .Select(x => x.NotionPageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var article in approved.Where(x => !publishedIds.Contains(x.NotionPageId)))
        {
            result.Add(new BlogPublishingQueueItemDto(
                article.NotionPageId,
                article.Title,
                article.Slug,
                article.State,
                article.HumanReviewed,
                article.KeywordPrincipal,
                article.Intent,
                article.MetaTitle,
                article.MetaDescription,
                article.ClientViewUrl,
                article.LastEditedAt,
                "readyToPublish",
                true,
                null,
                null,
                null,
                null));
        }

        foreach (var post in published)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var preview = await _notionBlogClient.GetPreviewAsync(post.NotionPageId, cancellationToken);
                var currentFingerprint = global::SenorArroz.Application.Features.BlogPublishing.BlogContentFingerprint.Compute(preview);
                var publishedFingerprint = global::SenorArroz.Application.Features.BlogPublishing.BlogContentFingerprint.Compute(post);
                var hasChanges = !string.Equals(currentFingerprint, publishedFingerprint, StringComparison.Ordinal);
                var canRepublish = preview.HumanReviewed
                    && (string.Equals(preview.State, "Publicado", StringComparison.Ordinal)
                        || string.Equals(preview.State, "Aprobado", StringComparison.Ordinal));
                var status = canRepublish
                    ? hasChanges ? "changesPending" : "upToDate"
                    : "notReady";

                result.Add(new BlogPublishingQueueItemDto(
                    preview.NotionPageId,
                    preview.Title,
                    preview.Slug,
                    preview.State,
                    preview.HumanReviewed,
                    preview.KeywordPrincipal,
                    preview.Intent,
                    preview.MetaTitle,
                    preview.MetaDescription,
                    preview.ClientViewUrl,
                    preview.LastEditedAt,
                    status,
                    hasChanges,
                    $"{_configuration.SiteUrl}/blog/{post.Slug}",
                    post.PublishedAt,
                    post.UpdatedAt,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (BusinessException ex)
            {
                result.Add(new BlogPublishingQueueItemDto(
                    post.NotionPageId,
                    post.Title,
                    post.Slug,
                    "Publicado",
                    true,
                    post.KeywordPrincipal,
                    post.Intent,
                    post.MetaTitle,
                    post.MetaDescription,
                    null,
                    null,
                    "checkFailed",
                    false,
                    $"{_configuration.SiteUrl}/blog/{post.Slug}",
                    post.PublishedAt,
                    post.UpdatedAt,
                    ex.Message));
            }
        }

        var ordered = result
            .OrderBy(x => StatusOrder(x.PublicationStatus))
            .ThenByDescending(x => x.LastEditedAt ?? x.PublishedUpdatedAt ?? DateTime.MinValue)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var totalCount = ordered.Length;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new PagedResult<BlogPublishingQueueItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
        };
    }

    private static int StatusOrder(string status) => status switch
    {
        "changesPending" => 0,
        "readyToPublish" => 1,
        "notReady" => 2,
        "checkFailed" => 3,
        _ => 4,
    };
}
