-- Coordenadas en sucursal (modelo EF). Idempotente.
-- También incluido al inicio de Scripts/deliveryman-liquidation-schema.sql y en
-- Scripts/local-init-completo.sql (CREATE branch + ALTER de paridad).
--
-- Ejecutar en PostgreSQL si no usas `dotnet ef database update`
ALTER TABLE public.branch ADD COLUMN IF NOT EXISTS latitude numeric(10,6) NULL;
ALTER TABLE public.branch ADD COLUMN IF NOT EXISTS longitude numeric(10,6) NULL;
