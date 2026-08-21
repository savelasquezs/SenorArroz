BEGIN;

LOCK TABLE product_category, product, order_detail, loyalty_cycle_step,
    daily_promotion, daily_promotion_product, discount_code,
    delivery_app_product_mapping IN SHARE ROW EXCLUSIVE MODE;

UPDATE loyalty_cycle_step
SET gift_product_id = NULL
WHERE gift_product_id IN (SELECT id FROM product WHERE category_id = 15);

UPDATE daily_promotion
SET gift_product_id = NULL
WHERE gift_product_id IN (SELECT id FROM product WHERE category_id = 15);

UPDATE discount_code
SET gift_product_id = NULL
WHERE gift_product_id IN (SELECT id FROM product WHERE category_id = 15);

DELETE FROM daily_promotion_product
WHERE product_id IN (SELECT id FROM product WHERE category_id = 15);

DELETE FROM delivery_app_product_mapping
WHERE product_id IN (SELECT id FROM product WHERE category_id = 15);

DELETE FROM order_detail
WHERE product_id IN (SELECT id FROM product WHERE category_id = 15);

DELETE FROM product
WHERE category_id = 15;

DELETE FROM product_category
WHERE id = 15;

COMMIT;
