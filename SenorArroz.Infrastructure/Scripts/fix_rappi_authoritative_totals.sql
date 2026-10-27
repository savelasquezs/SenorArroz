BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

CREATE OR REPLACE FUNCTION public.update_order_totals() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    order_subtotal INTEGER;
    order_discount_total INTEGER;
    order_delivery_fee INTEGER;
    target_order_id INTEGER;
BEGIN
    IF TG_OP = 'DELETE' THEN
        target_order_id = OLD.order_id;
    ELSE
        target_order_id = NEW.order_id;
    END IF;

    IF EXISTS (SELECT 1 FROM public."order" o WHERE o.id = target_order_id
        AND o.order_source = 'rappi' AND o.external_fulfillment_provider = 'rappi'
        AND o.delivery_app_connection_id IS NOT NULL AND NULLIF(o.external_order_id, '') IS NOT NULL) THEN
        IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END IF;

    SELECT COALESCE(SUM(subtotal), 0) INTO order_subtotal FROM public.order_detail WHERE order_id = target_order_id;
    SELECT COALESCE(SUM(discount), 0) INTO order_discount_total FROM public.order_detail WHERE order_id = target_order_id;
    SELECT COALESCE(delivery_fee, 0) INTO order_delivery_fee FROM public."order" WHERE id = target_order_id;

    UPDATE public."order" SET subtotal = order_subtotal, discount_total = order_discount_total,
        total = order_subtotal + order_delivery_fee, updated_at = NOW()
    WHERE id = target_order_id;

    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END; $$;

CREATE OR REPLACE FUNCTION public.update_order_total_on_delivery_fee_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.order_source = 'rappi' AND NEW.external_fulfillment_provider = 'rappi'
        AND NEW.delivery_app_connection_id IS NOT NULL AND NULLIF(NEW.external_order_id, '') IS NOT NULL THEN
        RETURN NEW;
    END IF;
    IF OLD.delivery_fee IS DISTINCT FROM NEW.delivery_fee THEN
        NEW.total = NEW.subtotal + COALESCE(NEW.delivery_fee, 0);
    END IF;
    RETURN NEW;
END; $$;

COMMIT;
