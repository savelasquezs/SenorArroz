-- Índice para filtros de listado de pedidos por app y liquidación (POST /orders/search).
-- Ejecutar en producción una vez; idempotente.

CREATE INDEX IF NOT EXISTS idx_app_payment_app_settled_order ON app_payment (app_id, is_setted) INCLUDE (order_id);
