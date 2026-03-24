-- Marca temporal de última liquidación del día (ciclos múltiples / desbloqueo).
-- Idempotente: seguro ejecutar varias veces.

ALTER TABLE deliveryman_day_state
    ADD COLUMN IF NOT EXISTS last_liquidation_at_utc timestamp with time zone NULL;

COMMENT ON COLUMN deliveryman_day_state.last_liquidation_at_utc IS
    'UTC: fin de la última liquidación exitosa del día; abonos y entregas posteriores cuentan en el siguiente ciclo.';
