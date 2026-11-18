BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL app.system_scope = 'on';

LOCK TABLE public.whatsapp_commerce_session IN SHARE ROW EXCLUSIVE MODE;

UPDATE public.whatsapp_commerce_session
SET status = 'expired', commercial_status = 'no_sale',
    completed_at = COALESCE(completed_at, expires_at)
WHERE status = 'active' AND expires_at <= now()
  AND order_id IS NULL AND checkout_id IS NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM public.whatsapp_commerce_session WHERE status = 'active'
        GROUP BY tenant_id, conversation_id HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Reconcile multiple active commercial attempts before enforcing unique drafts';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_commerce_active_conversation
    ON public.whatsapp_commerce_session (tenant_id, conversation_id) WHERE status = 'active';

COMMIT;
