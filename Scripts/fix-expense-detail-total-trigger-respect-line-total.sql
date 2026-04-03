-- Producción: el trigger BEFORE INSERT/UPDATE en expense_detail no debe sobrescribir total de línea
-- cuando la API lo envía explícito (ej. factura con redondeo distinto a quantity × amount entero).
--
-- Sin este cambio, al guardar y volver a leer, total volvía a quantity * amount.

CREATE OR REPLACE FUNCTION public.calculate_expense_detail_total() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.total IS NULL THEN
        NEW.total := ROUND((NEW.quantity * NEW.amount)::numeric, 2);
    END IF;
    RETURN NEW;
END; $$;

-- El trigger calculate_expense_detail_total_trigger ya apunta a esta función; no hace falta recrearlo.
