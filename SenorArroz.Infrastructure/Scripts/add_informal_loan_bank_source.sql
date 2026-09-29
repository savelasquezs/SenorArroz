BEGIN;

ALTER TABLE public.branch_informal_loan
    ADD COLUMN IF NOT EXISTS bank_id integer;

ALTER TABLE public.cash_closure_bank_reconciliation
    ADD COLUMN IF NOT EXISTS informal_loan_deduction numeric(12,2) NOT NULL DEFAULT 0;

CREATE UNIQUE INDEX IF NOT EXISTS ux_bank_tenant_id_id
    ON public.bank (tenant_id, id);

DO $block$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_branch_informal_loan_bank'
          AND conrelid = 'public.branch_informal_loan'::regclass
    ) THEN
        ALTER TABLE public.branch_informal_loan
            ADD CONSTRAINT "FK_branch_informal_loan_bank"
            FOREIGN KEY (tenant_id, bank_id)
            REFERENCES public.bank (tenant_id, id)
            ON DELETE RESTRICT;
    END IF;
END
$block$;

CREATE INDEX IF NOT EXISTS "IX_branch_informal_loan_branch_bank_active"
    ON public.branch_informal_loan (branch_id, bank_id, deactivated_at);

COMMIT;
