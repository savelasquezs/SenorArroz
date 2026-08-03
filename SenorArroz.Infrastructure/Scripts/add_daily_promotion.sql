CREATE TABLE IF NOT EXISTS daily_promotion (
    id serial PRIMARY KEY,
    branch_id integer NOT NULL REFERENCES branch(id) ON DELETE CASCADE,
    created_by_user_id integer NULL REFERENCES "user"(id) ON DELETE SET NULL,
    type varchar(40) NOT NULL,
    gift_product_id integer NULL REFERENCES product(id) ON DELETE RESTRICT,
    discount_percentage numeric(5,2) NULL,
    discount_scope varchar(40) NULL,
    minimum_order_value integer NULL,
    is_active boolean NOT NULL DEFAULT false,
    starts_at timestamp without time zone NOT NULL,
    ends_at timestamp without time zone NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);

CREATE TABLE IF NOT EXISTS daily_promotion_product (
    id serial PRIMARY KEY,
    daily_promotion_id integer NOT NULL REFERENCES daily_promotion(id) ON DELETE CASCADE,
    product_id integer NOT NULL REFERENCES product(id) ON DELETE RESTRICT,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_daily_promotion_branch ON daily_promotion(branch_id);
CREATE INDEX IF NOT EXISTS idx_daily_promotion_created_by_user ON daily_promotion(created_by_user_id);
CREATE INDEX IF NOT EXISTS idx_daily_promotion_active ON daily_promotion(is_active);
CREATE INDEX IF NOT EXISTS idx_daily_promotion_starts_at ON daily_promotion(starts_at);
CREATE INDEX IF NOT EXISTS idx_daily_promotion_ends_at ON daily_promotion(ends_at);
CREATE INDEX IF NOT EXISTS idx_daily_promotion_active_lookup
    ON daily_promotion(branch_id, is_active, starts_at, ends_at);

CREATE INDEX IF NOT EXISTS idx_daily_promotion_product_promotion
    ON daily_promotion_product(daily_promotion_id);
CREATE INDEX IF NOT EXISTS idx_daily_promotion_product_product
    ON daily_promotion_product(product_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_daily_promotion_product_unique
    ON daily_promotion_product(daily_promotion_id, product_id);
