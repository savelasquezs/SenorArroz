-- Producción: pedidos exentos del bloqueo de cuadre por préstamo domicilio anticipado.
-- Ejecutar una vez en la base existente.

ALTER TABLE branch_informal_loan
    ALTER COLUMN concept TYPE character varying(500);

CREATE TABLE IF NOT EXISTS branch_informal_loan_exempt_order (
    loan_id integer NOT NULL,
    order_id integer NOT NULL,
    CONSTRAINT "PK_branch_informal_loan_exempt_order" PRIMARY KEY (loan_id, order_id),
    CONSTRAINT "FK_bioeo_loan" FOREIGN KEY (loan_id) REFERENCES branch_informal_loan (id) ON DELETE CASCADE,
    CONSTRAINT "FK_bioeo_order" FOREIGN KEY (order_id) REFERENCES "order" (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_branch_informal_loan_exempt_order_order_id"
    ON branch_informal_loan_exempt_order (order_id);
CREATE INDEX IF NOT EXISTS "IX_branch_informal_loan_exempt_order_loan_id"
    ON branch_informal_loan_exempt_order (loan_id);
