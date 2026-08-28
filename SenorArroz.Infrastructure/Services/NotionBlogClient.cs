using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BlogPublishing.DTOs;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Services;

public sealed class NotionBlogClient : INotionBlogClient
{
    private static readonly Regex PageIdRegex = new("[0-9a-fA-F]{32}", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly IBlogPublishingConfiguration _configuration;

    public NotionBlogClient(HttpClient httpClient, IBlogPublishingConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<BlogArticleSummaryDto>> GetApprovedArticlesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureNotionConfigured();
        var result = new List<BlogArticleSummaryDto>();
        string? cursor = null;

        do
        {
            var payload = new
            {
                filter = new
                {
                    and = new object[]
                    {
                        new { property = "Estado", select = new { equals = "Aprobado" } },
                        new { property = "Revisión humana", checkbox = new { equals = true } },
                    },
                },
                sorts = new[]
                {
                    new { timestamp = "last_edited_time", direction = "descending" },
                },
                page_size = 100,
                start_cursor = cursor,
            };

            using var request = CreateRequest(
                HttpMethod.Post,
                $"https://api.notion.com/v1/data_sources/{Uri.EscapeDataString(_configuration.NotionDataSourceId)}/query");
            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            using var document = await ReadJsonAsync(response, "consultar los artículos aprobados", cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("results", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (var page in pages.EnumerateArray())
                {
                    var summary = ParseSummary(page);
                    if (summary.State == "Aprobado" && summary.HumanReviewed)
                        result.Add(summary);
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hasMoreElement) && hasMoreElement.GetBoolean();
            cursor = hasMore && root.TryGetProperty("next_cursor", out var cursorElement) && cursorElement.ValueKind == JsonValueKind.String
                ? cursorElement.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return result;
    }

    public async Task<BlogArticlePreviewDto> GetPreviewAsync(
        string notionPageId,
        CancellationToken cancellationToken = default)
    {
        EnsureNotionConfigured();
        var normalizedPageId = NormalizePageId(notionPageId);
        using var request = CreateRequest(HttpMethod.Get, $"https://api.notion.com/v1/pages/{normalizedPageId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadJsonAsync(response, "leer el artículo de Notion", cancellationToken);
        var summary = ParseSummary(document.RootElement);

        if (string.IsNullOrWhiteSpace(summary.ClientViewUrl))
            throw new BusinessException("El artículo aprobado no tiene configurada la propiedad 'Vista cliente'.");

        var clientPageId = ExtractPageId(summary.ClientViewUrl);
        var warnings = new List<string>();
        var blocks = await ReadBlocksAsync(clientPageId, warnings, 0, cancellationToken);

        return new BlogArticlePreviewDto(
            summary.NotionPageId,
            summary.Title,
            summary.Slug,
            summary.State,
            summary.HumanReviewed,
            summary.KeywordPrincipal,
            summary.Intent,
            summary.MetaTitle?.Trim() ?? string.Empty,
            summary.MetaDescription?.Trim() ?? string.Empty,
            summary.ClientViewUrl,
            blocks,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            summary.LastEditedAt);
    }

    public async Task MarkPublishedAsync(
        string notionPageId,
        string publicUrl,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureNotionConfigured();
        var payload = new
        {
            properties = new Dictionary<string, object>
            {
                ["Estado"] = new { select = new { name = "Publicado" } },
                ["URL publicada"] = new { url = publicUrl },
                ["Fecha publicación"] = new { date = new { start = publishedAtUtc.ToString("yyyy-MM-dd") } },
            },
        };

        using var request = CreateRequest(HttpMethod.Patch, $"https://api.notion.com/v1/pages/{NormalizePageId(notionPageId)}");
        request.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var _ = await ReadJsonAsync(response, "actualizar el estado del artículo en Notion", cancellationToken);
    }

    private async Task<List<BlogBlockDto>> ReadBlocksAsync(
        string parentId,
        List<string> warnings,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 8)
        {
            warnings.Add("El contenido contiene más de 8 niveles de anidación y fue truncado en la vista previa.");
            return [];
        }

        var result = new List<BlogBlockDto>();
        string? cursor = null;
        do
        {
            var url = $"https://api.notion.com/v1/blocks/{NormalizePageId(parentId)}/children?page_size=100";
            if (!string.IsNullOrWhiteSpace(cursor))
                url += $"&start_cursor={Uri.EscapeDataString(cursor)}";

            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            using var document = await ReadJsonAsync(response, "leer el contenido de la vista cliente", cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("results", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in blocks.EnumerateArray())
                {
                    var mapped = await MapBlockAsync(block, warnings, depth, cancellationToken);
                    if (mapped is not null)
                        result.Add(mapped);
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hasMoreElement) && hasMoreElement.GetBoolean();
            cursor = hasMore && root.TryGetProperty("next_cursor", out var cursorElement) && cursorElement.ValueKind == JsonValueKind.String
                ? cursorElement.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return result;
    }

    private async Task<BlogBlockDto?> MapBlockAsync(
        JsonElement block,
        List<string> warnings,
        int depth,
        CancellationToken cancellationToken)
    {
        var type = block.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var supported = type is "paragraph" or "heading_1" or "heading_2" or "heading_3"
            or "bulleted_list_item" or "numbered_list_item" or "quote" or "divider" or "table" or "table_row";
        if (!supported)
        {
            warnings.Add($"Bloque de Notion no soportado para publicación: {type}.");
            return null;
        }

        var richText = new List<BlogRichTextDto>();
        var cells = new List<List<BlogRichTextDto>>();
        if (type == "table_row")
        {
            if (block.TryGetProperty(type, out var row) && row.TryGetProperty("cells", out var cellElements))
            {
                foreach (var cell in cellElements.EnumerateArray())
                    cells.Add(ParseRichText(cell));
            }
        }
        else if (type != "divider" && block.TryGetProperty(type, out var data) && data.TryGetProperty("rich_text", out var textElements))
        {
            richText = ParseRichText(textElements);
        }

        var children = new List<BlogBlockDto>();
        var hasChildren = block.TryGetProperty("has_children", out var hasChildrenElement) && hasChildrenElement.GetBoolean();
        if (hasChildren && block.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
            children = await ReadBlocksAsync(idElement.GetString()!, warnings, depth + 1, cancellationToken);

        return new BlogBlockDto
        {
            Type = type,
            RichText = richText,
            Cells = cells,
            Children = children,
        };
    }

    private static List<BlogRichTextDto> ParseRichText(JsonElement elements)
    {
        var result = new List<BlogRichTextDto>();
        if (elements.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in elements.EnumerateArray())
        {
            var text = item.TryGetProperty("plain_text", out var plainText) ? plainText.GetString() ?? string.Empty : string.Empty;
            var href = item.TryGetProperty("href", out var hrefElement) && hrefElement.ValueKind == JsonValueKind.String
                ? hrefElement.GetString()
                : null;
            var bold = false;
            var italic = false;
            var underline = false;
            var strikethrough = false;
            var code = false;
            if (item.TryGetProperty("annotations", out var annotations))
            {
                bold = ReadBoolean(annotations, "bold");
                italic = ReadBoolean(annotations, "italic");
                underline = ReadBoolean(annotations, "underline");
                strikethrough = ReadBoolean(annotations, "strikethrough");
                code = ReadBoolean(annotations, "code");
            }

            result.Add(new BlogRichTextDto(text, href, bold, italic, underline, strikethrough, code));
        }

        return result;
    }

    private static BlogArticleSummaryDto ParseSummary(JsonElement page)
    {
        var pageId = page.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
        var title = ReadTextProperty(page, "Tema");
        var slug = ReadTextProperty(page, "Slug");
        var state = ReadSelectProperty(page, "Estado");
        var reviewed = ReadCheckboxProperty(page, "Revisión humana");
        var keyword = NullIfEmpty(ReadTextProperty(page, "Keyword principal"));
        var intent = NullIfEmpty(ReadSelectProperty(page, "Intención"));
        var metaTitle = NullIfEmpty(ReadTextProperty(page, "Meta title"));
        var metaDescription = NullIfEmpty(ReadTextProperty(page, "Meta description"));
        var clientViewUrl = NullIfEmpty(ReadUrlProperty(page, "Vista cliente"));
        DateTime? lastEdited = null;
        if (page.TryGetProperty("last_edited_time", out var editedElement)
            && editedElement.ValueKind == JsonValueKind.String
            && DateTime.TryParse(editedElement.GetString(), out var parsed))
        {
            lastEdited = parsed.ToUniversalTime();
        }

        return new BlogArticleSummaryDto(
            pageId,
            title,
            slug,
            state,
            reviewed,
            keyword,
            intent,
            metaTitle,
            metaDescription,
            clientViewUrl,
            lastEdited);
    }

    private static string ReadTextProperty(JsonElement page, string propertyName)
    {
        if (!TryGetProperty(page, propertyName, out var property))
            return string.Empty;

        foreach (var collectionName in new[] { "title", "rich_text" })
        {
            if (!property.TryGetProperty(collectionName, out var elements) || elements.ValueKind != JsonValueKind.Array)
                continue;
            return string.Concat(elements.EnumerateArray().Select(item =>
                item.TryGetProperty("plain_text", out var plainText) ? plainText.GetString() : string.Empty)).Trim();
        }

        return string.Empty;
    }

    private static string ReadSelectProperty(JsonElement page, string propertyName)
    {
        if (!TryGetProperty(page, propertyName, out var property)
            || !property.TryGetProperty("select", out var select)
            || select.ValueKind == JsonValueKind.Null)
            return string.Empty;
        return select.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
    }

    private static bool ReadCheckboxProperty(JsonElement page, string propertyName) =>
        TryGetProperty(page, propertyName, out var property)
        && property.TryGetProperty("checkbox", out var checkbox)
        && checkbox.ValueKind is JsonValueKind.True or JsonValueKind.False
        && checkbox.GetBoolean();

    private static string ReadUrlProperty(JsonElement page, string propertyName)
    {
        if (!TryGetProperty(page, propertyName, out var property)
            || !property.TryGetProperty("url", out var url)
            || url.ValueKind != JsonValueKind.String)
            return string.Empty;
        return url.GetString() ?? string.Empty;
    }

    private static bool TryGetProperty(JsonElement page, string name, out JsonElement property)
    {
        property = default;
        return page.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(name, out property);
    }

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.NotionApiKey);
        request.Headers.TryAddWithoutValidation("Notion-Version", _configuration.NotionApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new BusinessException($"No fue posible {operation}. Notion respondió {(int)response.StatusCode}.");
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new BusinessException($"Notion devolvió una respuesta inválida al intentar {operation}.");
        }
    }

    private void EnsureNotionConfigured()
    {
        if (string.IsNullOrWhiteSpace(_configuration.NotionApiKey))
            throw new BusinessException("La integración del blog no tiene configurado NOTION_API_KEY.");
        if (string.IsNullOrWhiteSpace(_configuration.NotionDataSourceId))
            throw new BusinessException("La integración del blog no tiene configurado NOTION_BLOG_DATA_SOURCE_ID.");
    }

    private static string ExtractPageId(string url)
    {
        var compact = url.Replace("-", string.Empty, StringComparison.Ordinal);
        var matches = PageIdRegex.Matches(compact);
        if (matches.Count == 0)
            throw new BusinessException("La propiedad 'Vista cliente' no contiene una URL válida de Notion.");
        return NormalizePageId(matches[^1].Value);
    }

    private static string NormalizePageId(string value)
    {
        var compact = value.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        if (compact.Length != 32 || !Guid.TryParseExact(compact, "N", out var parsed))
            throw new BusinessException("El identificador del artículo de Notion no es válido.");
        return parsed.ToString("D");
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
