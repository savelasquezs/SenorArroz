-- Ajusta gastos para cantidad decimal y total por línea
-- quantity: numeric(12,2)
-- total por detalle: numeric(12,2)
-- total encabezado: suma de totales por detalle

BEGIN;

ALTER TABLE public.expense_detail
    ALTER COLUMN quantity TYPE numeric(12,2) USING quantity::numeric(12,2),
    ALTER COLUMN total TYPE numeric(12,2) USING total::numeric(12,2);

ALTER TABLE public.expense_header
    ALTER COLUMN total TYPE numeric(12,2) USING total::numeric(12,2);

UPDATE public.expense_detail
SET total = ROUND((quantity * amount::numeric), 2)
WHERE total IS NULL OR total <> ROUND((quantity * amount::numeric), 2);

UPDATE public.expense_header eh
SET total = x.sum_total
FROM (
    SELECT
        header_id,
        ROUND(SUM(COALESCE(total, quantity * amount::numeric)), 2) AS sum_total
    FROM public.expense_detail
    GROUP BY header_id
) x
WHERE eh.id = x.header_id;

COMMIT;
