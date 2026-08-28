BEGIN;

CREATE TABLE IF NOT EXISTS blog_post (
    id serial PRIMARY KEY,
    tenant_id integer NOT NULL DEFAULT 1,
    notion_page_id varchar(64) NOT NULL,
    title varchar(240) NOT NULL,
    slug varchar(180) NOT NULL,
    meta_title varchar(240) NOT NULL,
    meta_description varchar(500) NOT NULL,
    keyword_principal varchar(240),
    intent varchar(40),
    content_json jsonb NOT NULL,
    published_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_blog_post_tenant_id_positive CHECK (tenant_id > 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_blog_post_tenant_notion_page
    ON blog_post (tenant_id, notion_page_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_blog_post_tenant_slug
    ON blog_post (tenant_id, slug);

CREATE INDEX IF NOT EXISTS ix_blog_post_tenant_published_at
    ON blog_post (tenant_id, published_at DESC, id DESC);

COMMIT;

-- Verificación opcional:
-- SELECT id, tenant_id, notion_page_id, slug, published_at, updated_at
-- FROM blog_post
-- ORDER BY published_at DESC;
