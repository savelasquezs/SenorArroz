-- Producción: tope COP para descuento "domicilio gratis" en POS (por sucursal).
-- Idempotente en PostgreSQL 9.1+ (IF NOT EXISTS en ADD COLUMN desde PG 9.1... actually IF NOT EXISTS for columns is PG 11+)

ALTER TABLE branch
    ADD COLUMN IF NOT EXISTS max_free_delivery_discount integer NOT NULL DEFAULT 3000;
