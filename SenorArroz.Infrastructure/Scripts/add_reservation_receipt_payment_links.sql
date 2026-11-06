-- Additive prerequisites for atomic initial reservation receipts.
-- Apply before deploying the API. Safe to repeat. No historical backfill.
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

ALTER TABLE public.reservation_deposit
    ADD COLUMN IF NOT EXISTS cash_applied_to_order boolean NOT NULL DEFAULT false;
ALTER TABLE public.app_payment
    ADD COLUMN IF NOT EXISTS source_reservation_deposit_id integer;

CREATE UNIQUE INDEX IF NOT EXISTS ux_reservation_deposit_tenant_id_id
    ON public.reservation_deposit(tenant_id, id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_app_payment_source_reservation_deposit
    ON public.app_payment(tenant_id, source_reservation_deposit_id)
    WHERE source_reservation_deposit_id IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_app_payment_source_reservation_deposit'
          AND conrelid = 'public.app_payment'::regclass) THEN
        ALTER TABLE public.app_payment
            ADD CONSTRAINT fk_app_payment_source_reservation_deposit
            FOREIGN KEY (tenant_id, source_reservation_deposit_id)
            REFERENCES public.reservation_deposit(tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $$;
