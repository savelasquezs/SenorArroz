-- IVA opcional en gastos: total encabezado = suma líneas + vat_amount.
-- Producción: ejecutar una vez.

ALTER TABLE public.expense_header
    ADD COLUMN IF NOT EXISTS vat_amount numeric(12, 2) NOT NULL DEFAULT 0;

CREATE OR REPLACE FUNCTION public.recalc_expense_header_total(p_header_id integer) RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    line_sum numeric(12, 2);
    vat_part numeric(12, 2);
BEGIN
    SELECT COALESCE(SUM(total), 0) INTO line_sum
    FROM public.expense_detail
    WHERE header_id = p_header_id;

    SELECT COALESCE(vat_amount, 0) INTO vat_part
    FROM public.expense_header
    WHERE id = p_header_id;

    UPDATE public.expense_header
    SET total = line_sum + vat_part,
        updated_at = NOW()
    WHERE id = p_header_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.update_expense_header_total() RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    target_header_id integer;
BEGIN
    IF TG_OP = 'DELETE' THEN
        target_header_id := OLD.header_id;
    ELSE
        target_header_id := NEW.header_id;
    END IF;

    PERFORM public.recalc_expense_header_total(target_header_id);

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    ELSE
        RETURN NEW;
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION public.update_expense_header_total_on_vat_change() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.vat_amount IS DISTINCT FROM NEW.vat_amount THEN
        PERFORM public.recalc_expense_header_total(NEW.id);
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS update_expense_header_total_on_vat ON public.expense_header;
CREATE TRIGGER update_expense_header_total_on_vat
    AFTER UPDATE OF vat_amount ON public.expense_header
    FOR EACH ROW
    EXECUTE FUNCTION public.update_expense_header_total_on_vat_change();

-- Recalcular totales existentes
UPDATE public.expense_header eh
SET total = COALESCE((SELECT SUM(ed.total) FROM public.expense_detail ed WHERE ed.header_id = eh.id), 0)
    + COALESCE(eh.vat_amount, 0);
