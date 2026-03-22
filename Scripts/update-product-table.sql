-- Actualizar tabla public.product en Railway (u otro PostgreSQL).
-- Idempotente: ADD COLUMN IF NOT EXISTS.
--
-- Documentación del flujo recomendado (railway connect + \i): RAILWAY-MIGRATIONS.md
--
-- Resumen:
--   cd senorArrozAPI && railway link   (servicio PostgreSQL)
--   railway connect postgres
--   \i Scripts/update-product-table.sql
--   En Windows, si falla: \i 'C:/ruta/completa/a/senorArrozAPI/Scripts/update-product-table.sql'
--
-- Alternativa: DATABASE_URL pública del panel → psql "$DATABASE_URL" -f Scripts/update-product-table.sql
--
BEGIN;

ALTER TABLE public.product
    ADD COLUMN IF NOT EXISTS weight_grams integer NULL;

COMMENT ON COLUMN public.product.weight_grams IS 'Peso en gramos (opcional).';

COMMIT;
