ALTER TABLE "order"
ADD COLUMN IF NOT EXISTS free_delivery_requested boolean NOT NULL DEFAULT false;

