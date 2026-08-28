# Publicación automatizada del blog

## Objetivo

El blog público se administra desde el frontend interno de Señor Arroz y solo puede publicarlo un usuario `Superadmin`.

Notion es la capa editorial. PostgreSQL conserva el snapshot que realmente se publica. La web pública nunca consulta Notion directamente.

## Flujo

1. El artículo se prepara en la base editorial de Notion.
2. Solo se lista en el admin cuando `Estado = Aprobado` y `Revisión humana = true`.
3. El Superadmin solicita una vista previa.
4. `RestauranteAPI` vuelve a leer la ficha de Notion y la propiedad `Vista cliente`.
5. El backend convierte los bloques permitidos a JSON estructurado y reporta bloques incompatibles.
6. El Superadmin confirma `Publicar`.
7. El backend valida nuevamente el artículo y hace upsert del snapshot en `blog_post`.
8. Después de persistir, actualiza Notion a `Publicado`, registra la URL pública y la fecha.
9. `senorarroz.com` lee exclusivamente los snapshots publicados mediante los endpoints protegidos con la API key del storefront.

## Seguridad

- `NOTION_API_KEY` existe únicamente en el backend.
- El frontend administrativo nunca recibe ni envía el token de Notion.
- Los endpoints de administración usan `[Authorize(Roles = "Superadmin")]`.
- Los endpoints públicos del backend usan `StorefrontApiKeyOptions.Scheme`; el navegador público llega a ellos solo mediante Next.js.
- El contenido no usa HTML crudo. Solo se aceptan bloques y segmentos de texto tipados.
- Enlaces se vuelven a validar en los renderers y solo se activan para rutas internas, `http` o `https`.

## Multitenancy transitorio

`blog_post` nace con `tenant_id` y claves únicas por tenant. Mientras el plan multitenant general sigue en migración, el tenant actual se resuelve únicamente en backend mediante `BLOG_TENANT_ID` y usa `1` por defecto. El frontend nunca puede elegirlo.

Cuando exista `ICurrentTenant`, se reemplazará la resolución de configuración sin cambiar el esquema de `blog_post`.

## Variables de entorno

- `NOTION_API_KEY`: token de una integración interna de Notion con acceso a la base editorial.
- `NOTION_BLOG_DATA_SOURCE_ID`: ID del data source de `Pipeline editorial SEO`.
- `NOTION_API_VERSION`: opcional; por defecto `2026-03-11`.
- `BLOG_SITE_URL`: opcional; por defecto `https://senorarroz.com`.
- `BLOG_TENANT_ID`: opcional; por defecto `1` durante la transición single-tenant.

La integración de producción de Notion debe compartirse explícitamente con la base `Pipeline editorial SEO`. La conexión de ChatGPT a Notion no sustituye el token del backend.

## Propiedades requeridas en Notion

- `Tema`
- `Estado`
- `Revisión humana`
- `Slug`
- `Keyword principal`
- `Intención`
- `Meta title`
- `Meta description`
- `Vista cliente`
- `URL publicada`
- `Fecha publicación`

## Bloques soportados en el MVP

- párrafos
- encabezados 1–3
- listas con viñetas y numeradas
- citas
- separadores
- tablas y filas de tabla
- texto enriquecido: enlaces, negrita, cursiva, subrayado, tachado y código inline

Si la vista cliente contiene un tipo de bloque no soportado, se muestra una advertencia en preview y la publicación se bloquea.

## Persistencia e idempotencia

`blog_post` mantiene un único snapshot por `(tenant_id, notion_page_id)` y un slug único por tenant. Repetir la publicación del mismo artículo actualiza el snapshot y conserva la fecha de publicación original.

La base de datos se actualiza antes que Notion. Si Notion falla después del commit, el artículo sigue publicado y el admin recibe una advertencia; repetir la publicación es seguro y vuelve a intentar la sincronización.

## Orden obligatorio de despliegue

Este repositorio no usa migraciones EF para cambios de esquema.

1. Ejecutar en PostgreSQL/Railway `SenorArroz.Infrastructure/Scripts/add_blog_publishing.sql`.
2. Verificar que existe `blog_post` y sus índices.
3. Configurar las variables de entorno de Notion.
4. Desplegar el backend.
5. Desplegar el frontend administrativo.
6. Desplegar la web pública.

No desplegar el backend nuevo antes de aplicar el script SQL.
