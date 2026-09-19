-- Manual benefit timestamps are instants produced by IClock.UtcNow. The original
-- schema used timestamp without time zone, which makes Npgsql materialize them as
-- DateTimeKind.Unspecified and prevents unrelated order updates from being saved.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'order'
          AND column_name = 'manual_benefit_granted_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE "order"
            ALTER COLUMN manual_benefit_granted_at TYPE timestamp with time zone
            USING manual_benefit_granted_at AT TIME ZONE 'UTC';
    END IF;
END $$;

COMMENT ON COLUMN "order".manual_benefit_granted_at
    IS 'UTC instant when a manual benefit was granted.';
