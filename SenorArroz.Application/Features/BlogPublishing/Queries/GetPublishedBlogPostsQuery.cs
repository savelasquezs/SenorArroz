using MediatR;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BlogPublishing.Queries;

public sealed record GetPublishedBlogPostsQuery : IRequest<IReadOnlyList<BlogPublishedPostDto>>;

public sealed class GetPublishedBlogPostsHandler
    : IRequestHandler<GetPublishedBlogPostsQuery, IReadOnlyList<BlogPublishedPostDto>>
{
    private readonly IBlogPostRepository _repository;

    public GetPublishedBlogPostsHandler(IBlogPostRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<BlogPublishedPostDto>> Handle(
        GetPublishedBlogPostsQuery request,
        CancellationToken cancellationToken)
    {
        var posts = await _repository.GetPublishedAsync(cancellationToken);
        return posts.Select(BlogPublishedPostDto.FromEntity).ToArray();
    }
}

public sealed record GetPublishedBlogPostBySlugQuery(string Slug) : IRequest<BlogPublishedPostDto?>;

public sealed class GetPublishedBlogPostBySlugHandler
    : IRequestHandler<GetPublishedBlogPostBySlugQuery, BlogPublishedPostDto?>
{
    private readonly IBlogPostRepository _repository;

    public GetPublishedBlogPostBySlugHandler(IBlogPostRepository repository)
    {
        _repository = repository;
    }

    public async Task<BlogPublishedPostDto?> Handle(
        GetPublishedBlogPostBySlugQuery request,
        CancellationToken cancellationToken)
    {
        var post = await _repository.GetBySlugAsync(request.Slug.Trim(), cancellationToken);
        return post is null ? null : BlogPublishedPostDto.FromEntity(post);
    }
}
