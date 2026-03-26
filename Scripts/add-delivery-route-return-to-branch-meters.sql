-- Columna de distancia aproximada última entrega → sucursal al cerrar ruta.
ALTER TABLE delivery_route
    ADD COLUMN IF NOT EXISTS return_to_branch_meters integer NULL;

COMMENT ON COLUMN delivery_route.return_to_branch_meters IS
    'Metros en línea recta (Haversine) desde la dirección de la última entrega hasta la sucursal.';
