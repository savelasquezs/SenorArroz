CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_dloc_deliveryman_recorded_id
    ON public.deliveryman_location(deliveryman_id, recorded_at, id);
