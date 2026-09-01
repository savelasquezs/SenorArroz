BEGIN;

ALTER TABLE wompi_payment_attempt
    DROP CONSTRAINT IF EXISTS wompi_payment_attempt_app_payment_id_fkey;

ALTER TABLE wompi_payment_attempt
    ADD CONSTRAINT wompi_payment_attempt_app_payment_id_fkey
    FOREIGN KEY (app_payment_id)
    REFERENCES app_payment(id)
    ON DELETE SET NULL;

ALTER TABLE delivery_route_proposal_stop
    DROP CONSTRAINT IF EXISTS delivery_route_proposal_stop_order_id_fkey;

ALTER TABLE delivery_route_proposal_stop
    ADD CONSTRAINT delivery_route_proposal_stop_order_id_fkey
    FOREIGN KEY (order_id)
    REFERENCES "order"(id)
    ON DELETE CASCADE;

COMMIT;
