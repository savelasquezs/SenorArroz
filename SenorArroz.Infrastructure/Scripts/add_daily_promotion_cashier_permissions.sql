ALTER TABLE daily_promotion
    ADD COLUMN IF NOT EXISTS created_by_user_id integer NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_daily_promotion_created_by_user'
          AND conrelid = 'daily_promotion'::regclass
    ) THEN
        ALTER TABLE daily_promotion
            ADD CONSTRAINT fk_daily_promotion_created_by_user
            FOREIGN KEY (created_by_user_id)
            REFERENCES "user"(id)
            ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_daily_promotion_created_by_user
    ON daily_promotion(created_by_user_id);

-- La vigencia se controla por el intervalo starts_at/ends_at. Pueden existir
-- promociones activas no superpuestas de fechas diferentes para una sucursal.
DROP INDEX IF EXISTS ux_daily_promotion_one_active_per_branch;

COMMENT ON COLUMN daily_promotion.created_by_user_id IS
    'Usuario que creo la promocion; permite limitar la edicion del cajero a sus propios registros.';
