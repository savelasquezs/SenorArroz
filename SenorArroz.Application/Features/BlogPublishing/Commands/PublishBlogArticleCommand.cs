using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BlogPublishing.Commands;

public sealed record PublishBlogArticleCommand(string NotionPageId) : IRequest<BlogPublishResultDto>;

public sealed class PublishBlogArticleHandler
    : IRequestHandler<PublishBlogArticleCommand, BlogPublishResultDto>
{
    private static readonly Regex SlugRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private readonly INotionBlogClient _notionBlogClient;
    private readonly IBlogPostRepository _repository;
    private readonly IBlogPublishingConfiguration _configuration;

    public PublishBlogArticleHandler(
        INotionBlogClient notionBlogClient,
        IBlogPostRepository repository,
        IBlogPublishingConfiguration configuration)
    {
        _notionBlogClient = notionBlogClient;
        _repository = repository;
        _configuration = configuration;
    }

    public async Task<BlogPublishResultDto> Handle(
        PublishBlogArticleCommand request,
        CancellationToken cancellationToken)
    {
        var preview = await _notionBlogClient.GetPreviewAsync(request.NotionPageId, cancellationToken);
        var publishedPosts = await _repository.GetPublishedAsync(cancellationToken);
        var existingPost = publishedPosts.FirstOrDefault(x =>
            string.Equals(x.NotionPageId, preview.NotionPageId, StringComparison.OrdinalIgnoreCase));

        Validate(preview, existingPost is not null);

        var existingSlug = await _repository.GetBySlugAsync(preview.Slug, cancellationToken);
        if (existingSlug is not null
            && !string.Equals(existingSlug.NotionPageId, preview.NotionPageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Ya existe otro artículo publicado con ese slug. Corrige el Slug en Notion antes de publicar.");
        }

        var now = DateTime.UtcNow;
        var post = new BlogPost
        {
            NotionPageId = preview.NotionPageId,
            Title = preview.Title.Trim(),
            Slug = preview.Slug.Trim(),
            MetaTitle = preview.MetaTitle.Trim(),
            MetaDescription = preview.MetaDescription.Trim(),
            KeywordPrincipal = preview.KeywordPrincipal?.Trim(),
            Intent = preview.Intent?.Trim(),
            ContentJson = JsonSerializer.Serialize(preview.Blocks),
            PublishedAt = existingPost?.PublishedAt ?? now,
        };

        var saved = await _repository.UpsertAsync(post, cancellationToken);
        var publicUrl = $"{_configuration.SiteUrl}/blog/{saved.Slug}";
        var warnings = new List<string>();
        try
        {
            await _notionBlogClient.MarkPublishedAsync(preview.NotionPageId, publicUrl, saved.PublishedAt, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            warnings.Add("El artículo quedó publicado, pero Notion no pudo actualizarse a 'Publicado'. Puedes reintentar la publicación sin crear duplicados.");
        }

        return new BlogPublishResultDto(BlogPublishedPostDto.FromEntity(saved), publicUrl, warnings);
    }

    private static void Validate(BlogArticlePreviewDto preview, bool isRepublish)
    {
        if (!preview.HumanReviewed)
            throw new BusinessException("El artículo debe tener marcada la revisión humana antes de publicarse.");

        var validState = string.Equals(preview.State, "Aprobado", StringComparison.Ordinal)
            || (isRepublish && string.Equals(preview.State, "Publicado", StringComparison.Ordinal));
        if (!validState)
        {
            throw new BusinessException(isRepublish
                ? "Para republicar, el artículo debe estar en estado Publicado o Aprobado."
                : "Solo se pueden publicar artículos aprobados y con revisión humana marcada.");
        }

        if (string.IsNullOrWhiteSpace(preview.Title))
            throw new BusinessException("El artículo no tiene título.");
        if (preview.Title.Length > 240)
            throw new BusinessException("El título del artículo supera 240 caracteres.");
        if (string.IsNullOrWhiteSpace(preview.Slug) || preview.Slug.Length > 180 || !SlugRegex.IsMatch(preview.Slug))
            throw new BusinessException("El slug debe usar únicamente minúsculas, números y guiones.");
        if (string.IsNullOrWhiteSpace(preview.MetaTitle) || preview.MetaTitle.Length > 240)
            throw new BusinessException("El artículo necesita un Meta title válido.");
        if (string.IsNullOrWhiteSpace(preview.MetaDescription) || preview.MetaDescription.Length > 500)
            throw new BusinessException("El artículo necesita una Meta description válida.");
        if (preview.Blocks.Count == 0)
            throw new BusinessException("La Vista cliente está vacía.");
        if (preview.Warnings.Count > 0)
            throw new BusinessException("La Vista cliente contiene bloques que aún no son compatibles con el publicador. Revísalos en la vista previa antes de publicar.");
    }
}
