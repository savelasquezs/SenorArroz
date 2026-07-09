ALTER TABLE loyalty_cycle_step
ADD COLUMN IF NOT EXISTS reward_type varchar(40) NULL,
ADD COLUMN IF NOT EXISTS gift_product_id integer NULL,
ADD COLUMN IF NOT EXISTS discount_percentage numeric(5,2) NULL,
ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_loyalty_cycle_step_gift_product'
    ) THEN
        ALTER TABLE loyalty_cycle_step
        ADD CONSTRAINT fk_loyalty_cycle_step_gift_product
        FOREIGN KEY (gift_product_id) REFERENCES product(id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_loyalty_cycle_step_active
    ON loyalty_cycle_step(is_active);
CREATE INDEX IF NOT EXISTS idx_loyalty_cycle_step_gift_product
    ON loyalty_cycle_step(gift_product_id);
