-- IVA individual por línea de gasto.
-- Ejecutar antes de desplegar el backend que consulta expense_detail.include_vat.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'expense_detail'
          AND column_name = 'include_vat'
    ) THEN
        ALTER TABLE public.expense_detail
            ADD COLUMN include_vat boolean NOT NULL DEFAULT false;

        -- El modelo anterior aplicaba el IVA a todas las líneas del encabezado.
        UPDATE public.expense_detail AS ed
        SET include_vat = true
        FROM public.expense_header AS eh
        WHERE eh.id = ed.header_id
          AND COALESCE(eh.vat_amount, 0) > 0;
    END IF;
END
$$;

COMMENT ON COLUMN public.expense_detail.include_vat IS
    'Indica si la línea integra la base gravable del IVA del comprobante.';
