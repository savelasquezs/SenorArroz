-- Marks bank payments created from app settlements so cash-register global totals do not count them as new period income.
-- Run once on PostgreSQL production or local environments.

ALTER TABLE bank_payment
    ADD COLUMN IF NOT EXISTS is_app_settlement boolean NOT NULL DEFAULT false;

ALTER TABLE bank_payment
    ADD COLUMN IF NOT EXISTS app_settlement_source_payment_ids text;

-- Conservative backfill for legacy one-to-one app settlements.
-- Aggregated multi-app settlements cannot be inferred safely from existing rows.
WITH one_to_one_matches AS (
    SELECT
        bp.id AS bank_payment_id,
        MIN(ap.id) AS app_payment_id
    FROM bank_payment bp
    INNER JOIN app_payment ap ON ap.order_id = bp.order_id
    INNER JOIN app a ON a.id = ap.app_id AND a.bank_id = bp.bank_id
    WHERE bp.amount = ap.amount
      AND ap.is_setted = true
      AND bp.source_reservation_deposit_id IS NULL
      AND (bp.is_app_settlement = false OR bp.app_settlement_source_payment_ids IS NULL)
    GROUP BY bp.id
    HAVING COUNT(*) = 1
)
UPDATE bank_payment bp
SET is_app_settlement = true,
    app_settlement_source_payment_ids = '[' || m.app_payment_id::text || ']'
FROM one_to_one_matches m
WHERE bp.id = m.bank_payment_id;

-- Manual audit for legacy aggregate settlements:
-- review bank_payment rows still unmarked where one bank deposit may equal the sum of multiple settled app_payment rows.
-- Those require manual confirmation before setting is_app_settlement/app_settlement_source_payment_ids.
