using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public sealed class BlogPostRepository : IBlogPostRepository
{
    private readonly ApplicationDbContext _context;
    private readonly IBlogPublishingConfiguration _configuration;

    public BlogPostRepository(ApplicationDbContext context, IBlogPublishingConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<BlogPost>> GetPublishedAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, tenant_id, notion_page_id, title, slug, meta_title, meta_description,
                   keyword_principal, intent, content_json::text, published_at, created_at, updated_at
            FROM blog_post
            WHERE tenant_id = @tenant_id
            ORDER BY published_at DESC, id DESC;
            """;
        return await QueryAsync(sql, command => AddParameter(command, "tenant_id", _configuration.TenantId), cancellationToken);
    }

    public async Task<BlogPost?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, tenant_id, notion_page_id, title, slug, meta_title, meta_description,
                   keyword_principal, intent, content_json::text, published_at, created_at, updated_at
            FROM blog_post
            WHERE tenant_id = @tenant_id AND slug = @slug
            LIMIT 1;
            """;
        var rows = await QueryAsync(sql, command =>
        {
            AddParameter(command, "tenant_id", _configuration.TenantId);
            AddParameter(command, "slug", slug);
        }, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<BlogPost> UpsertAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO blog_post (
                tenant_id, notion_page_id, title, slug, meta_title, meta_description,
                keyword_principal, intent, content_json, published_at, created_at, updated_at)
            VALUES (
                @tenant_id, @notion_page_id, @title, @slug, @meta_title, @meta_description,
                @keyword_principal, @intent, CAST(@content_json AS jsonb), @published_at, NOW(), NOW())
            ON CONFLICT (tenant_id, notion_page_id)
            DO UPDATE SET
                title = EXCLUDED.title,
                slug = EXCLUDED.slug,
                meta_title = EXCLUDED.meta_title,
                meta_description = EXCLUDED.meta_description,
                keyword_principal = EXCLUDED.keyword_principal,
                intent = EXCLUDED.intent,
                content_json = EXCLUDED.content_json,
                published_at = EXCLUDED.published_at,
                updated_at = NOW()
            RETURNING id, tenant_id, notion_page_id, title, slug, meta_title, meta_description,
                      keyword_principal, intent, content_json::text, published_at, created_at, updated_at;
            """;

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "tenant_id", _configuration.TenantId);
            AddParameter(command, "notion_page_id", post.NotionPageId);
            AddParameter(command, "title", post.Title);
            AddParameter(command, "slug", post.Slug);
            AddParameter(command, "meta_title", post.MetaTitle);
            AddParameter(command, "meta_description", post.MetaDescription);
            AddParameter(command, "keyword_principal", post.KeywordPrincipal);
            AddParameter(command, "intent", post.Intent);
            AddParameter(command, "content_json", post.ContentJson);
            AddParameter(command, "published_at", post.PublishedAt);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("No fue posible recuperar el artículo después de publicarlo.");
            return Map(reader);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task<IReadOnlyList<BlogPost>> QueryAsync(
        string sql,
        Action<DbCommand> configure,
        CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure(command);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<BlogPost>();
            while (await reader.ReadAsync(cancellationToken))
                result.Add(Map(reader));
            return result;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static BlogPost Map(DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        TenantId = reader.GetInt32(1),
        NotionPageId = reader.GetString(2),
        Title = reader.GetString(3),
        Slug = reader.GetString(4),
        MetaTitle = reader.GetString(5),
        MetaDescription = reader.GetString(6),
        KeywordPrincipal = reader.IsDBNull(7) ? null : reader.GetString(7),
        Intent = reader.IsDBNull(8) ? null : reader.GetString(8),
        ContentJson = reader.GetString(9),
        PublishedAt = reader.GetDateTime(10),
        CreatedAt = reader.GetDateTime(11),
        UpdatedAt = reader.GetDateTime(12),
    };

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
