-- Script para crear la tabla deliveryman_advance
-- Gestión de abonos/adelantos a domiciliarios

CREATE TABLE IF NOT EXISTS deliveryman_advance (
    id SERIAL PRIMARY KEY,
    deliveryman_id INTEGER NOT NULL,
    amount DECIMAL(10,2) NOT NULL CHECK (amount > 0),
    notes TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by INTEGER NOT NULL,
    branch_id INTEGER NOT NULL,
    
    -- Foreign Keys (nombres correctos en singular)
    CONSTRAINT fk_deliveryman_advance_deliveryman 
        FOREIGN KEY (deliveryman_id) 
        REFERENCES "user"(id)   -- Cambio: user en singular y con comillas
        ON DELETE RESTRICT,
    
    CONSTRAINT fk_deliveryman_advance_created_by 
        FOREIGN KEY (created_by) 
        REFERENCES "user"(id)   -- Cambio: user en singular y con comillas
        ON DELETE RESTRICT,
    
    CONSTRAINT fk_deliveryman_advance_branch 
        FOREIGN KEY (branch_id) 
        REFERENCES branch(id) 
        ON DELETE RESTRICT
);

-- Índices para mejorar el rendimiento
CREATE INDEX IF NOT EXISTS idx_deliveryman_advance_deliveryman ON deliveryman_advance(deliveryman_id);
CREATE INDEX IF NOT EXISTS idx_deliveryman_advance_created_at ON deliveryman_advance(created_at);
CREATE INDEX IF NOT EXISTS idx_deliveryman_advance_branch ON deliveryman_advance(branch_id);
CREATE INDEX IF NOT EXISTS idx_deliveryman_advance_created_by ON deliveryman_advance(created_by);

-- Trigger para actualizar updated_at automáticamente
CREATE OR REPLACE FUNCTION update_deliveryman_advance_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_deliveryman_advance_updated_at ON deliveryman_advance;

CREATE TRIGGER trigger_deliveryman_advance_updated_at
    BEFORE UPDATE ON deliveryman_advance
    FOR EACH ROW
    EXECUTE FUNCTION update_deliveryman_advance_updated_at();

-- Comentarios para documentación
COMMENT ON TABLE deliveryman_advance IS 'Tabla para registrar abonos/adelantos realizados a domiciliarios';
COMMENT ON COLUMN deliveryman_advance.deliveryman_id IS 'ID del domiciliario que recibe el abono';
COMMENT ON COLUMN deliveryman_advance.amount IS 'Monto del abono en pesos colombianos';
COMMENT ON COLUMN deliveryman_advance.notes IS 'Comentarios o notas adicionales sobre el abono';
COMMENT ON COLUMN deliveryman_advance.created_by IS 'ID del usuario (admin/cajero) que creó el abono';
COMMENT ON COLUMN deliveryman_advance.branch_id IS 'ID de la sucursal donde se realizó el abono';