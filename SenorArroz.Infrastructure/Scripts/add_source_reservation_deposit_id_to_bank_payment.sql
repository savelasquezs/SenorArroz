-- Adds the link between promoted reservation deposits and bank payments.
-- Run once on PostgreSQL production or local environments.

ALTER TABLE bank_payment
    ADD COLUMN IF NOT EXISTS source_reservation_deposit_id integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_bank_payment_source_reservation_deposit'
    ) THEN
        ALTER TABLE bank_payment
            ADD CONSTRAINT fk_bank_payment_source_reservation_deposit
            FOREIGN KEY (source_reservation_deposit_id)
            REFERENCES reservation_deposit(id)
            ON DELETE RESTRICT;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_bank_payment_source_reservation_deposit_id
    ON bank_payment(source_reservation_deposit_id)
    WHERE source_reservation_deposit_id IS NOT NULL;
