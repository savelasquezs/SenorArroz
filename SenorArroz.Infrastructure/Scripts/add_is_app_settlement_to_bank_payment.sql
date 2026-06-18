-- Marks bank payments created from app settlements so cash-register global totals do not count them as new period income.
-- Run once on PostgreSQL production or local environments.

ALTER TABLE bank_payment
    ADD COLUMN IF NOT EXISTS is_app_settlement boolean NOT NULL DEFAULT false;

-- Conservative backfill for legacy one-to-one app settlements.
-- Aggregated multi-app settlements cannot be inferred safely from existing rows.
UPDATE bank_payment bp
SET is_app_settlement = true
FROM app_payment ap
INNER JOIN app a ON a.id = ap.app_id
WHERE bp.order_id = ap.order_id
  AND bp.bank_id = a.bank_id
  AND bp.amount = ap.amount
  AND ap.is_setted = true
  AND bp.source_reservation_deposit_id IS NULL
  AND bp.is_app_settlement = false;
