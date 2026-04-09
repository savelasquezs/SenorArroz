-- ─────────────────────────────────────────────────────────────────────────────
-- GPS Tracking: tabla de ubicaciones en tiempo real de domiciliarios
-- Ejecutar en producción DESPUÉS de haber desplegado la nueva versión del backend
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS public.deliveryman_location (
    id                bigserial PRIMARY KEY,
    deliveryman_id    integer    NOT NULL REFERENCES public."user"(id)           ON DELETE CASCADE,
    delivery_route_id integer             REFERENCES public.delivery_route(id)   ON DELETE SET NULL,
    latitude          numeric(10,6) NOT NULL,
    longitude         numeric(10,6) NOT NULL,
    recorded_at       timestamp with time zone NOT NULL,
    created_at        timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_dloc_deliveryman
    ON public.deliveryman_location(deliveryman_id);

CREATE INDEX IF NOT EXISTS idx_dloc_route
    ON public.deliveryman_location(delivery_route_id);

CREATE INDEX IF NOT EXISTS idx_dloc_recorded
    ON public.deliveryman_location(recorded_at DESC);
