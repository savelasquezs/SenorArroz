-- Producción: tope COP para descuento "domicilio gratis" en POS (por sucursal).
-- Idempotente: compatible con PostgreSQL 9.x+ (sin usar ADD COLUMN IF NOT EXISTS, introducido en PG 11).

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND table_name = 'branch'
          AND column_name = 'max_free_delivery_discount'
    ) THEN
        ALTER TABLE branch
            ADD COLUMN max_free_delivery_discount integer NOT NULL DEFAULT 3000;
    END IF;
END
$$;
