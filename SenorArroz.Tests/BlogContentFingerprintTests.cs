using System.Text.Json;
using SenorArroz.Application.Features.BlogPublishing;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class BlogContentFingerprintTests
{
    [Fact]
    public void Same_published_snapshot_and_preview_have_same_fingerprint()
    {
        var blocks = new List<BlogBlockDto>
        {
            new()
            {
                Type = "paragraph",
                RichText = [new BlogRichTextDto("Texto del artículo", null, false, false, false, false, false)],
            },
        };
        var preview = Preview(blocks);
        var post = new BlogPost
        {
            Title = preview.Title,
            Slug = preview.Slug,
            MetaTitle = preview.MetaTitle,
            MetaDescription = preview.MetaDescription,
            KeywordPrincipal = preview.KeywordPrincipal,
            Intent = preview.Intent,
            ContentJson = JsonSerializer.Serialize(blocks),
        };

        Assert.Equal(BlogContentFingerprint.Compute(post), BlogContentFingerprint.Compute(preview));
    }

    [Fact]
    public void Editing_client_content_changes_fingerprint()
    {
        var original = Preview([
            new BlogBlockDto
            {
                Type = "paragraph",
                RichText = [new BlogRichTextDto("Texto original", null, false, false, false, false, false)],
            },
        ]);
        var edited = Preview([
            new BlogBlockDto
            {
                Type = "paragraph",
                RichText = [new BlogRichTextDto("Texto modificado", null, false, false, false, false, false)],
            },
        ]);

        Assert.NotEqual(BlogContentFingerprint.Compute(original), BlogContentFingerprint.Compute(edited));
    }

    [Fact]
    public void Editorial_state_does_not_change_fingerprint()
    {
        var approved = Preview([], "Aprobado");
        var published = Preview([], "Publicado");

        Assert.Equal(BlogContentFingerprint.Compute(approved), BlogContentFingerprint.Compute(published));
    }

    private static BlogArticlePreviewDto Preview(IReadOnlyList<BlogBlockDto> blocks, string state = "Publicado") => new(
        "notion-page",
        "¿Cuánto arroz pedir?",
        "cuanto-arroz-pedir",
        state,
        true,
        "cuánto arroz pedir",
        "Comercial",
        "¿Cuánto arroz pedir? | Señor Arroz",
        "Guía para escoger la presentación adecuada.",
        "https://notion.test/client-view",
        blocks,
        [],
        DateTime.UtcNow);
}
