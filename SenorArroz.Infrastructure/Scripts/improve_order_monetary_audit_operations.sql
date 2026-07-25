-- Agrupa todos los cambios de una misma edición de pedido sin cambiar el esquema.
-- Es idempotente: reemplaza únicamente las funciones ya usadas por los triggers.

CREATE OR REPLACE FUNCTION public.audit_order_row_changes() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_changed_at timestamp with time zone := NOW();
    v_before jsonb;
    v_after jsonb;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_before := jsonb_build_object(
            'id', OLD.id,
            'status', OLD.status,
            'total', OLD.total,
            'subtotal', OLD.subtotal,
            'discount_total', OLD.discount_total,
            'delivery_fee', COALESCE(OLD.delivery_fee, 0),
            'paid_in_store_cash_amount', COALESCE(OLD.paid_in_store_cash_amount, 0),
            'lines', '[]'::jsonb
        );

        PERFORM public.audit_insert_log(
            OLD.branch_id, 'order', OLD.id, 'deleted', v_changed_at,
            format('Pedido #%s eliminado. Total anterior: %s', OLD.id, to_char(COALESCE(OLD.total, 0), 'FM999G999G999G990')),
            jsonb_build_object('total_before', COALESCE(OLD.total, 0), 'total_after', 0, 'difference', -COALESCE(OLD.total, 0), 'lines_affected', '[]'::jsonb),
            v_before, NULL, jsonb_build_object('trigger', TG_NAME, 'operation_id', txid_current()::text)
        );
        RETURN OLD;
    END IF;

    v_after := public.audit_order_snapshot(NEW.id);
    v_before := jsonb_build_object('id', OLD.id, 'status', OLD.status, 'total', OLD.total, 'subtotal', OLD.subtotal, 'discount_total', OLD.discount_total, 'delivery_fee', COALESCE(OLD.delivery_fee, 0), 'paid_in_store_cash_amount', COALESCE(OLD.paid_in_store_cash_amount, 0));

    IF OLD.status IS DISTINCT FROM NEW.status AND NEW.status = 'cancelled' THEN
        PERFORM public.audit_insert_log(
            NEW.branch_id, 'order', NEW.id, 'cancelled', v_changed_at,
            format('Pedido #%s cancelado. Total afectado: %s', NEW.id, to_char(COALESCE(OLD.total, 0), 'FM999G999G999G990')),
            jsonb_build_object('total_before', COALESCE(OLD.total, 0), 'total_after', COALESCE(NEW.total, 0), 'difference', COALESCE(NEW.total, 0) - COALESCE(OLD.total, 0), 'lines_affected', '[]'::jsonb, 'impact_type', 'removed_from_sales'),
            v_before, v_after, jsonb_build_object('trigger', TG_NAME, 'operation_id', txid_current()::text, 'cancelled_reason', NEW.cancelled_reason)
        );
        RETURN NEW;
    END IF;

    IF OLD.delivery_fee IS DISTINCT FROM NEW.delivery_fee
       OR OLD.paid_in_store_cash_amount IS DISTINCT FROM NEW.paid_in_store_cash_amount
       OR OLD.subtotal IS DISTINCT FROM NEW.subtotal
       OR OLD.discount_total IS DISTINCT FROM NEW.discount_total
       OR OLD.total IS DISTINCT FROM NEW.total
       OR OLD.cancelled_reason IS DISTINCT FROM NEW.cancelled_reason THEN
        PERFORM public.audit_insert_log(
            NEW.branch_id, 'order', NEW.id, 'modified', v_changed_at,
            format('Pedido #%s modificado monetariamente. %s -> %s', NEW.id, COALESCE(OLD.total, 0), COALESCE(NEW.total, 0)),
            jsonb_build_object('total_before', COALESCE(OLD.total, 0), 'total_after', COALESCE(NEW.total, 0), 'difference', COALESCE(NEW.total, 0) - COALESCE(OLD.total, 0), 'lines_affected', '[]'::jsonb),
            v_before, v_after, jsonb_build_object('trigger', TG_NAME, 'operation_id', txid_current()::text)
        );
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION public.audit_order_detail_changes() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_order_id integer;
    v_branch_id integer;
    v_changed_at timestamp with time zone := NOW();
    v_after jsonb;
    v_before_total numeric;
    v_after_total numeric;
    v_old_subtotal numeric := 0;
    v_new_subtotal numeric := 0;
    v_summary text;
BEGIN
    v_order_id := COALESCE(NEW.order_id, OLD.order_id);
    SELECT o.branch_id INTO v_branch_id FROM public."order" o WHERE o.id = v_order_id;
    v_after := public.audit_order_snapshot(v_order_id);
    IF TG_OP <> 'INSERT' THEN
        v_old_subtotal := COALESCE(OLD.subtotal, 0);
    END IF;
    IF TG_OP <> 'DELETE' THEN
        v_new_subtotal := COALESCE(NEW.subtotal, 0);
    END IF;
    v_after_total := COALESCE((v_after ->> 'total')::numeric, 0);
    v_before_total := v_after_total - v_new_subtotal + v_old_subtotal;

    IF TG_OP = 'INSERT' THEN
        v_summary := format('Pedido #%s: producto %s agregado. Cantidad %s, subtotal %s', v_order_id, NEW.product_id, NEW.quantity, COALESCE(NEW.subtotal, 0));
    ELSIF TG_OP = 'DELETE' THEN
        v_summary := format('Pedido #%s: producto %s eliminado. Cantidad %s, subtotal %s', v_order_id, OLD.product_id, OLD.quantity, COALESCE(OLD.subtotal, 0));
    ELSIF OLD.product_id IS DISTINCT FROM NEW.product_id THEN
        v_summary := format('Pedido #%s: producto %s cambiado por %s', v_order_id, OLD.product_id, NEW.product_id);
    ELSIF OLD.quantity IS DISTINCT FROM NEW.quantity THEN
        v_summary := format('Pedido #%s: producto %s cambio cantidad %s->%s', v_order_id, NEW.product_id, OLD.quantity, NEW.quantity);
    ELSIF OLD.unit_price IS DISTINCT FROM NEW.unit_price THEN
        v_summary := format('Pedido #%s: producto %s cambio valor %s->%s', v_order_id, NEW.product_id, OLD.unit_price, NEW.unit_price);
    ELSIF OLD.discount IS DISTINCT FROM NEW.discount THEN
        v_summary := format('Pedido #%s: producto %s cambio descuento %s->%s', v_order_id, NEW.product_id, COALESCE(OLD.discount, 0), COALESCE(NEW.discount, 0));
    ELSE
        v_summary := format('Pedido #%s: producto %s modifico subtotal %s->%s', v_order_id, COALESCE(NEW.product_id, OLD.product_id), v_old_subtotal, v_new_subtotal);
    END IF;

    PERFORM public.audit_insert_log(
        v_branch_id, 'order', v_order_id, 'modified', v_changed_at, v_summary,
        jsonb_build_object(
            'total_before', v_before_total,
            'total_after', v_after_total,
            'difference', v_after_total - v_before_total,
            'lines_affected', jsonb_build_array(jsonb_strip_nulls(jsonb_build_object(
                'product_id', COALESCE(NEW.product_id, OLD.product_id),
                'product_id_before', CASE WHEN TG_OP <> 'INSERT' THEN OLD.product_id END,
                'product_id_after', CASE WHEN TG_OP <> 'DELETE' THEN NEW.product_id END,
                'quantity_before', CASE WHEN TG_OP <> 'INSERT' THEN OLD.quantity END,
                'quantity_after', CASE WHEN TG_OP <> 'DELETE' THEN NEW.quantity END,
                'unit_price_before', CASE WHEN TG_OP <> 'INSERT' THEN OLD.unit_price END,
                'unit_price_after', CASE WHEN TG_OP <> 'DELETE' THEN NEW.unit_price END,
                'discount_before', CASE WHEN TG_OP <> 'INSERT' THEN OLD.discount END,
                'discount_after', CASE WHEN TG_OP <> 'DELETE' THEN NEW.discount END,
                'subtotal_before', CASE WHEN TG_OP <> 'INSERT' THEN OLD.subtotal END,
                'subtotal_after', CASE WHEN TG_OP <> 'DELETE' THEN NEW.subtotal END
            )))
        ),
        jsonb_build_object('id', v_order_id, 'total', v_before_total),
        v_after,
        jsonb_build_object('trigger', TG_NAME, 'operation_id', txid_current()::text, 'detail_operation', TG_OP)
    );

    RETURN COALESCE(NEW, OLD);
END;
$$;
