BEGIN;

ALTER TABLE public."user"
ADD COLUMN IF NOT EXISTS web_access_enabled boolean;

UPDATE public."user"
SET web_access_enabled = CASE
    WHEN lower(role::text) = 'deliveryman' THEN FALSE
    ELSE TRUE
END
WHERE web_access_enabled IS NULL;

ALTER TABLE public."user"
ALTER COLUMN web_access_enabled SET DEFAULT TRUE;

ALTER TABLE public."user"
ALTER COLUMN web_access_enabled SET NOT NULL;

COMMENT ON COLUMN public."user".web_access_enabled IS
'Permite acceso web a domiciliarios; los demas roles siempre conservan acceso web.';

COMMIT;
