BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL app.system_scope = 'on';

ALTER TABLE public.whatsapp_conversation
    ADD COLUMN IF NOT EXISTS branch_line_id integer REFERENCES public.whatsapp_branch_setting(id) ON DELETE RESTRICT;

UPDATE public.whatsapp_conversation c
SET branch_line_id = s.id
FROM public.whatsapp_branch_setting s
WHERE s.tenant_id = c.tenant_id AND s.branch_id = c.branch_id AND s.coexistence_enabled
  AND c.channel_setting_id IS NULL AND c.branch_line_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM public.whatsapp_branch_setting legacy
                  WHERE legacy.tenant_id = c.tenant_id AND legacy.branch_id = c.branch_id AND NOT legacy.coexistence_enabled);

CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_branch_setting_kind
    ON public.whatsapp_branch_setting(branch_id, coexistence_enabled);
ALTER TABLE public.whatsapp_branch_setting DROP CONSTRAINT IF EXISTS whatsapp_branch_setting_branch_id_key;
DROP INDEX IF EXISTS public.idx_whatsapp_branch_setting_branch;

CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_conversation_branch_line_phone
    ON public.whatsapp_conversation(branch_line_id, phone_number)
    WHERE branch_line_id IS NOT NULL AND phone_number IS NOT NULL AND phone_number <> '';
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_conversation_branch_line_user
    ON public.whatsapp_conversation(branch_line_id, whatsapp_user_id)
    WHERE branch_line_id IS NOT NULL AND whatsapp_user_id IS NOT NULL AND whatsapp_user_id <> '';

DROP INDEX IF EXISTS public.idx_whatsapp_conversation_branch_phone;
DROP INDEX IF EXISTS public.uq_whatsapp_conversation_branch_user_id;
CREATE UNIQUE INDEX idx_whatsapp_conversation_branch_phone
    ON public.whatsapp_conversation(branch_id, phone_number)
    WHERE channel_setting_id IS NULL AND branch_line_id IS NULL AND phone_number IS NOT NULL AND phone_number <> '';
CREATE UNIQUE INDEX uq_whatsapp_conversation_branch_user_id
    ON public.whatsapp_conversation(branch_id, whatsapp_user_id)
    WHERE channel_setting_id IS NULL AND branch_line_id IS NULL AND whatsapp_user_id IS NOT NULL AND whatsapp_user_id <> '';

COMMIT;
