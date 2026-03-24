-- Liquidación domiciliarios + columnas branch. Idempotente.
-- Bootstrap local completo: ver también Scripts/local-init-completo.sql (incluye este bloque).
--
-- Branch: coordenadas (modelo EF / joins con bank→branch). Sin esto PostgreSQL falla con "no existe la columna b2.latitude".
ALTER TABLE public.branch
    ADD COLUMN IF NOT EXISTS latitude numeric(10,6) NULL,
    ADD COLUMN IF NOT EXISTS longitude numeric(10,6) NULL;

-- Abonos domiciliario: método de pago, banco, gasto vinculado
ALTER TABLE public.deliveryman_advance
    ADD COLUMN IF NOT EXISTS payment_method integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS bank_id integer NULL REFERENCES public.bank (id),
    ADD COLUMN IF NOT EXISTS expense_header_id integer NULL REFERENCES public.expense_header (id);

CREATE INDEX IF NOT EXISTS idx_deliveryman_advance_bank ON public.deliveryman_advance (bank_id);
CREATE INDEX IF NOT EXISTS idx_deliveryman_advance_expense ON public.deliveryman_advance (expense_header_id);

-- Gastos asociados a domiciliario
ALTER TABLE public.expense_header
    ADD COLUMN IF NOT EXISTS deliveryman_id integer NULL REFERENCES public."user" (id);

CREATE INDEX IF NOT EXISTS idx_expense_header_deliveryman ON public.expense_header (deliveryman_id);

-- Estado de liquidación del día
CREATE TABLE IF NOT EXISTS public.deliveryman_day_state (
    id serial PRIMARY KEY,
    branch_id integer NOT NULL REFERENCES public.branch (id),
    deliveryman_id integer NOT NULL REFERENCES public."user" (id),
    date date NOT NULL,
    liquidation_mode integer NOT NULL DEFAULT 0,
    blocked boolean NOT NULL DEFAULT false,
    unlocked_at timestamp with time zone NULL,
    unlocked_by_id integer NULL REFERENCES public."user" (id),
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_deliveryman_day_state_branch_dm_date UNIQUE (branch_id, deliveryman_id, date)
);

CREATE INDEX IF NOT EXISTS idx_deliveryman_day_state_branch_date ON public.deliveryman_day_state (branch_id, date);
