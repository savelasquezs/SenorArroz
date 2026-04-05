-- Producción: ampliar profile_image_url para URLs de Firebase Storage.
ALTER TABLE "user" ALTER COLUMN profile_image_url TYPE character varying(2000);
