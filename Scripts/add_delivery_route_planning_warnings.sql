-- planning_warnings: textos de aviso al consolidar la ruta (sucursal/pedidos sin coords, fallo Google, etc.).
-- Ejecutar una vez en PostgreSQL.

ALTER TABLE delivery_route
    ADD COLUMN IF NOT EXISTS planning_warnings character varying(2000);

ALTER TABLE delivery_route
    ALTER COLUMN complex_access_buffer_seconds SET DEFAULT 480;
