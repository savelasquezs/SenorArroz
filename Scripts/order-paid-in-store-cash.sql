-- Pedido: efectivo cobrado en sucursal (domiciliario no cobra en entrega; cuadre de caja).
-- Ejecutar en producción antes/después del deploy del backend que usa estas columnas.

ALTER TABLE public."order" ADD COLUMN IF NOT EXISTS paid_in_store_cash boolean NOT NULL DEFAULT false;
ALTER TABLE public."order" ADD COLUMN IF NOT EXISTS paid_in_store_cash_at timestamp with time zone NULL;
ALTER TABLE public."order" ADD COLUMN IF NOT EXISTS paid_in_store_cash_amount integer NULL;
