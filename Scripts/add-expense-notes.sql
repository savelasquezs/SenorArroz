-- Notas opcionales en comprobantes de gasto (cabecera) y en cada línea.
-- Ejecutar en producción (Railway) una vez.

ALTER TABLE public.expense_header
    ADD COLUMN IF NOT EXISTS notes character varying(2000);

ALTER TABLE public.expense_detail
    ADD COLUMN IF NOT EXISTS notes character varying(1000);
