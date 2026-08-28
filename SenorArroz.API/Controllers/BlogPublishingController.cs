using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.API.Security;
using SenorArroz.Application.Features.BlogPublishing.Commands;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Application.Features.BlogPublishing.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/blog-publishing")]
[Authorize(Roles = "Superadmin")]
public sealed class BlogPublishingController : ControllerBase
{
    private readonly IMediator _mediator;

    public BlogPublishingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("queue")]
    public async Task<ActionResult<ApiResponse<PagedResult<BlogPublishingQueueItemDto>>>> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetBlogPublishingQueueQuery(page, pageSize), cancellationToken);
        return Ok(ApiResponse<PagedResult<BlogPublishingQueueItemDto>>.SuccessResponse(result));
    }

    [HttpGet("approved")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BlogArticleSummaryDto>>>> GetApproved(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetApprovedBlogArticlesQuery(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BlogArticleSummaryDto>>.SuccessResponse(result));
    }

    [HttpGet("{notionPageId}/preview")]
    public async Task<ActionResult<ApiResponse<BlogArticlePreviewDto>>> Preview(
        string notionPageId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBlogArticlePreviewQuery(notionPageId), cancellationToken);
        return Ok(ApiResponse<BlogArticlePreviewDto>.SuccessResponse(result));
    }

    [HttpPost("{notionPageId}/publish")]
    public async Task<ActionResult<ApiResponse<BlogPublishResultDto>>> Publish(
        string notionPageId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PublishBlogArticleCommand(notionPageId), cancellationToken);
        return Ok(ApiResponse<BlogPublishResultDto>.SuccessResponse(result, "Artículo publicado correctamente."));
    }
}

[ApiController]
[Route("api/public/blog")]
[Authorize(AuthenticationSchemes = StorefrontApiKeyOptions.Scheme)]
public sealed class PublicBlogController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicBlogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BlogPublishedPostSummaryDto>>>> GetPosts(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublishedBlogPostsQuery(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BlogPublishedPostSummaryDto>>.SuccessResponse(result));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<BlogPublishedPostDto>>> GetPost(
        string slug,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublishedBlogPostBySlugQuery(slug), cancellationToken);
        if (result is null)
            return NotFound(ApiResponse<BlogPublishedPostDto>.ErrorResponse("Artículo no encontrado."));
        return Ok(ApiResponse<BlogPublishedPostDto>.SuccessResponse(result));
    }
}
