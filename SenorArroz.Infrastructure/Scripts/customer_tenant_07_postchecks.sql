DO $$
DECLARE failures text[] := ARRAY[]::text[];
BEGIN
 IF EXISTS(SELECT 1 FROM customer_phone WHERE active GROUP BY tenant_id,phone_normalized HAVING count(DISTINCT customer_id)>1) THEN failures:=array_append(failures,'duplicate phones'); END IF;
 IF EXISTS(SELECT 1 FROM "order" o JOIN customer_merge_history h ON h.old_customer_id=o.customer_id) THEN failures:=array_append(failures,'orders reference absorbed customers'); END IF;
 IF EXISTS(SELECT 1 FROM address a JOIN customer_merge_history h ON h.old_customer_id=a.customer_id) THEN failures:=array_append(failures,'addresses reference absorbed customers'); END IF;
 IF EXISTS(SELECT 1 FROM whatsapp_conversation w JOIN customer_merge_history h ON h.old_customer_id=w.customer_id) THEN failures:=array_append(failures,'conversations reference absorbed customers'); END IF;
 IF EXISTS(SELECT 1 FROM whatsapp_commerce_session w JOIN customer_merge_history h ON h.old_customer_id=w.customer_id) THEN failures:=array_append(failures,'commerce sessions reference absorbed customers'); END IF;
 IF EXISTS(SELECT 1 FROM storefront_checkout s JOIN customer_merge_history h ON h.old_customer_id=s.customer_id) THEN failures:=array_append(failures,'checkouts reference absorbed customers'); END IF;
 IF EXISTS(SELECT 1 FROM delivery_app_connection d JOIN customer_merge_history h ON h.old_customer_id=d.customer_id) THEN failures:=array_append(failures,'delivery connections reference absorbed customers'); END IF;
 IF EXISTS(SELECT 1 FROM address_branch GROUP BY tenant_id,address_id,branch_id HAVING count(*)>1) THEN failures:=array_append(failures,'duplicate address branches'); END IF;
 IF EXISTS(SELECT 1 FROM customer_phone p LEFT JOIN customer c ON c.id=p.customer_id WHERE c.id IS NULL) THEN failures:=array_append(failures,'orphan phones'); END IF;
 IF EXISTS(SELECT 1 FROM address_branch ab LEFT JOIN address a ON a.id=ab.address_id LEFT JOIN branch b ON b.id=ab.branch_id WHERE a.id IS NULL OR b.id IS NULL) THEN failures:=array_append(failures,'orphan address branches'); END IF;
 IF EXISTS(SELECT 1 FROM customer c JOIN branch b ON b.id=c.branch_id WHERE c.tenant_id<>b.tenant_id)
    OR EXISTS(SELECT 1 FROM address a JOIN customer c ON c.id=a.customer_id WHERE a.tenant_id<>c.tenant_id)
    OR EXISTS(SELECT 1 FROM neighborhood n JOIN branch b ON b.id=n.branch_id WHERE n.tenant_id<>b.tenant_id)
    OR EXISTS(SELECT 1 FROM "order" o JOIN branch b ON b.id=o.branch_id WHERE o.tenant_id<>b.tenant_id)
    OR EXISTS(SELECT 1 FROM "order" o JOIN customer c ON c.id=o.customer_id WHERE o.tenant_id<>c.tenant_id)
    OR EXISTS(SELECT 1 FROM address_branch ab JOIN address a ON a.id=ab.address_id JOIN branch b ON b.id=ab.branch_id WHERE ab.tenant_id<>a.tenant_id OR ab.tenant_id<>b.tenant_id)
 THEN failures:=array_append(failures,'cross-tenant references'); END IF;
 IF EXISTS(SELECT 1 FROM "order" o LEFT JOIN customer c ON c.id=o.customer_id LEFT JOIN branch b ON b.id=o.branch_id WHERE (o.customer_id IS NOT NULL AND c.id IS NULL) OR b.id IS NULL)
    OR EXISTS(SELECT 1 FROM address a LEFT JOIN customer c ON c.id=a.customer_id WHERE c.id IS NULL)
 THEN failures:=array_append(failures,'orphan core references'); END IF;
 IF cardinality(failures)>0 THEN RAISE EXCEPTION 'POSTCHECK FAILED: %',array_to_string(failures,', '); END IF;
END $$;
SELECT 'postchecks_ok' status;
