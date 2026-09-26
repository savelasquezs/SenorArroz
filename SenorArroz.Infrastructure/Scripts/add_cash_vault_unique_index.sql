-- Enforce the invariant: at most one Caja Mayor Efectivo (BankType.CashVault = 1) per branch.
-- Run once in PostgreSQL before/with deploying the manual CashVault creation feature.

DO $$
BEGIN
    IF EXISTS (
        SELECT branch_id
        FROM bank
        WHERE type = 1
        GROUP BY branch_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'No se puede crear ux_bank_branch_cash_vault: existen sucursales con más de una Caja Mayor Efectivo';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_bank_branch_cash_vault
    ON bank (branch_id)
    WHERE type = 1;
