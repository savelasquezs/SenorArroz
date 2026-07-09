CREATE TABLE IF NOT EXISTS discount_code (
    id serial PRIMARY KEY,
    branch_id integer NOT NULL REFERENCES branch(id) ON DELETE CASCADE,
    code varchar(60) NOT NULL,
    type varchar(40) NOT NULL,
    gift_product_id integer NULL REFERENCES product(id) ON DELETE RESTRICT,
    discount_percentage numeric(5,2) NULL,
    starts_at timestamp without time zone NOT NULL,
    ends_at timestamp without time zone NULL,
    minimum_order_value integer NULL,
    is_active boolean NOT NULL DEFAULT false,
    label varchar(160) NOT NULL,
    description varchar(500) NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_discount_code_branch ON discount_code(branch_id);
CREATE INDEX IF NOT EXISTS idx_discount_code_active ON discount_code(is_active);
CREATE INDEX IF NOT EXISTS idx_discount_code_starts_at ON discount_code(starts_at);
CREATE INDEX IF NOT EXISTS idx_discount_code_ends_at ON discount_code(ends_at);
CREATE UNIQUE INDEX IF NOT EXISTS ux_discount_code_branch_code ON discount_code(branch_id, code);

ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_type varchar(40) NOT NULL DEFAULT 'None';
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_source_id integer NULL;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_code varchar(80) NULL;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_label varchar(250) NULL;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_reward_type varchar(40) NULL;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_amount numeric(10,2) NULL;
ALTER TABLE "order" ADD COLUMN IF NOT EXISTS applied_benefit_snapshot jsonb NULL;

CREATE INDEX IF NOT EXISTS idx_order_applied_benefit_type ON "order"(applied_benefit_type);
CREATE INDEX IF NOT EXISTS idx_order_applied_benefit_code ON "order"(applied_benefit_code);
