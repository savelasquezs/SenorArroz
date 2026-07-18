BEGIN;

-- Auditoria de beneficios discrecionales concedidos por administradores.
ALTER TABLE "order"
    ADD COLUMN IF NOT EXISTS manual_benefit_reason varchar(500),
    ADD COLUMN IF NOT EXISTS manual_benefit_granted_by_user_id integer,
    ADD COLUMN IF NOT EXISTS manual_benefit_granted_by_user_name varchar(150),
    ADD COLUMN IF NOT EXISTS manual_benefit_granted_at timestamp without time zone,
    ADD COLUMN IF NOT EXISTS manual_benefit_gift_product_id integer;

COMMENT ON COLUMN "order".manual_benefit_reason
    IS 'Motivo obligatorio del beneficio manual.';
COMMENT ON COLUMN "order".manual_benefit_granted_by_user_id
    IS 'Usuario que concedio el beneficio manual.';
COMMENT ON COLUMN "order".manual_benefit_granted_by_user_name
    IS 'Nombre historico del usuario que concedio el beneficio.';
COMMENT ON COLUMN "order".manual_benefit_granted_at
    IS 'Instante UTC en que se concedio el beneficio manual.';
COMMENT ON COLUMN "order".manual_benefit_gift_product_id
    IS 'Producto de la categoria Regalos concedido, cuando aplica.';

COMMIT;

-- Verificacion opcional:
-- SELECT column_name, data_type
-- FROM information_schema.columns
-- WHERE table_name = 'order' AND column_name LIKE 'manual_benefit_%'
-- ORDER BY column_name;
