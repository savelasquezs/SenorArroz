-- Run only after reviewing customer_tenant_05_prechecks.sql. Pass tenant in psql:
-- \set tenant_id 1
BEGIN;
SELECT pg_advisory_xact_lock(918273, :tenant_id);
SELECT set_config('senorarroz.merge_tenant_id', :'tenant_id', true);

CREATE TEMP TABLE merge_run(id uuid PRIMARY KEY);
INSERT INTO merge_run VALUES (gen_random_uuid());
INSERT INTO customer_merge_run(id,tenant_id,status) SELECT id,:tenant_id,'running' FROM merge_run;

DO $$ DECLARE merge_tenant integer := current_setting('senorarroz.merge_tenant_id')::integer; BEGIN
 IF EXISTS(SELECT 1 FROM customer c CROSS JOIN LATERAL unnest(ARRAY[c.phone1,c.phone2]) p(phone)
   WHERE c.tenant_id=merge_tenant AND nullif(btrim(p.phone),'') IS NOT NULL
   AND (CASE WHEN length(regexp_replace(p.phone,'[^0-9]','','g'))=12 AND regexp_replace(p.phone,'[^0-9]','','g') LIKE '57%'
             THEN substring(regexp_replace(p.phone,'[^0-9]','','g') FROM 3)
             ELSE regexp_replace(p.phone,'[^0-9]','','g') END) !~ '^(3[0-9]{9}|604[0-9]{7})$')
 THEN RAISE EXCEPTION 'Hay teléfonos inválidos; el merge fue cancelado'; END IF;
 IF EXISTS(SELECT 1 FROM customer c JOIN branch b ON b.id=c.branch_id WHERE c.tenant_id=merge_tenant AND c.tenant_id<>b.tenant_id)
    OR EXISTS(SELECT 1 FROM address a JOIN customer c ON c.id=a.customer_id WHERE a.tenant_id=merge_tenant AND a.tenant_id<>c.tenant_id)
    OR EXISTS(SELECT 1 FROM neighborhood n JOIN branch b ON b.id=n.branch_id WHERE n.tenant_id=merge_tenant AND n.tenant_id<>b.tenant_id)
    OR EXISTS(SELECT 1 FROM "order" o JOIN branch b ON b.id=o.branch_id WHERE o.tenant_id=merge_tenant AND o.tenant_id<>b.tenant_id)
 THEN RAISE EXCEPTION 'Hay relaciones entre tenants distintos; el merge fue cancelado'; END IF;
END $$;

DO $$
DECLARE unknown_fks text;
BEGIN
  SELECT string_agg(conrelid::regclass::text||'.'||a.attname, ', ')
  INTO unknown_fks
  FROM pg_constraint c
  JOIN unnest(c.conkey) WITH ORDINALITY k(attnum,ord) ON true
  JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum
  WHERE c.contype='f' AND c.confrelid='customer'::regclass
    AND (conrelid::regclass::text||'.'||a.attname) NOT IN
      ('address.customer_id','"order".customer_id','whatsapp_conversation.customer_id',
       'whatsapp_commerce_session.customer_id','storefront_checkout.customer_id',
       'delivery_app_connection.customer_id','customer_phone.customer_id',
       'customer_merge_history.old_customer_id','customer_merge_history.new_customer_id');
  IF unknown_fks IS NOT NULL THEN RAISE EXCEPTION 'FK hacia customer no contempladas: %',unknown_fks; END IF;
END $$;

CREATE TEMP TABLE merge_map(old_id integer PRIMARY KEY,new_id integer NOT NULL,tenant_id integer NOT NULL);
WITH RECURSIVE edges AS (
  SELECT DISTINCT a.tenant_id,a.customer_id a,b.customer_id b FROM customer_phone a
  JOIN customer_phone b USING(tenant_id,phone_normalized)
  WHERE a.tenant_id=:tenant_id AND a.active AND b.active AND a.customer_id<>b.customer_id
), reach(tenant_id,root,member) AS (
  SELECT tenant_id,a,a FROM edges UNION SELECT tenant_id,a,b FROM edges
  UNION SELECT r.tenant_id,r.root,e.b FROM reach r JOIN edges e ON e.tenant_id=r.tenant_id AND e.a=r.member
), groups AS (SELECT tenant_id,member,min(root) group_id FROM reach GROUP BY 1,2),
ranked AS (
 SELECT g.*,row_number() OVER(PARTITION BY g.tenant_id,g.group_id
   ORDER BY count(o.id) FILTER(WHERE o.status NOT IN('cancelled','awaiting_payment')) DESC,c.created_at,c.id) rn
 FROM groups g JOIN customer c ON c.id=g.member LEFT JOIN "order" o ON o.customer_id=c.id
 GROUP BY g.tenant_id,g.group_id,g.member,c.created_at,c.id
), winners AS (SELECT tenant_id,group_id,member winner FROM ranked WHERE rn=1)
INSERT INTO merge_map SELECT r.member,w.winner,r.tenant_id FROM ranked r JOIN winners w USING(tenant_id,group_id) WHERE r.member<>w.winner;

-- Conflicting non-empty customer identity is retained in audit details, never silently copied.
INSERT INTO customer_merge_history(tenant_id,old_customer_id,new_customer_id,merged_at,reason,merge_run_id,details)
SELECT m.tenant_id,m.old_id,m.new_id,now(),'duplicate_phone_identity',(SELECT id FROM merge_run),
 jsonb_build_object('old_name',o.name,'new_name',n.name,'old_whatsapp_user_id',o.whatsapp_user_id,'new_whatsapp_user_id',n.whatsapp_user_id)
FROM merge_map m JOIN customer o ON o.id=m.old_id JOIN customer n ON n.id=m.new_id
ON CONFLICT(tenant_id,old_customer_id) DO NOTHING;

-- Repoint every known direct reference.
UPDATE "order" x SET customer_id=m.new_id FROM merge_map m WHERE x.customer_id=m.old_id;
UPDATE whatsapp_conversation x SET customer_id=m.new_id FROM merge_map m WHERE x.customer_id=m.old_id;
UPDATE whatsapp_commerce_session x SET customer_id=m.new_id FROM merge_map m WHERE x.customer_id=m.old_id;
UPDATE storefront_checkout x SET customer_id=m.new_id FROM merge_map m WHERE x.customer_id=m.old_id;
UPDATE delivery_app_connection x SET customer_id=m.new_id FROM merge_map m WHERE x.customer_id=m.old_id;
UPDATE address x SET customer_id=m.new_id FROM merge_map m WHERE x.customer_id=m.old_id;

-- Build duplicate physical-address map after customer reassignment.
CREATE TEMP TABLE address_map(old_id integer PRIMARY KEY,new_id integer NOT NULL);
WITH normalized AS (
 SELECT a.*,customer_address_normalize(coalesce(nullif(a.normalized_address,''),nullif(a.original_address,''),a.address)) key
 FROM address a WHERE a.tenant_id=:tenant_id
), ranked AS (
 SELECT n.*,first_value(id) OVER(PARTITION BY tenant_id,customer_id,key ORDER BY is_primary DESC,(validated_at IS NOT NULL AND latitude IS NOT NULL AND longitude IS NOT NULL) DESC,created_at,id) winner
 FROM normalized n WHERE key<>''
)
INSERT INTO address_map SELECT id,winner FROM ranked WHERE id<>winner;

-- Any conflicting branch service must be resolved manually before merge.
INSERT INTO customer_merge_conflict(merge_run_id,tenant_id,conflict_type,entity_ids,details)
SELECT (SELECT id FROM merge_run),:tenant_id,'address_branch',array_agg(DISTINCT ab.address_id),
 jsonb_build_object('branch_id',ab.branch_id,'fees',array_agg(DISTINCT ab.delivery_fee),'neighborhoods',array_agg(DISTINCT ab.neighborhood_id),'coverage',array_agg(DISTINCT ab.is_covered))
FROM address_branch ab LEFT JOIN address_map am ON am.old_id=ab.address_id
WHERE ab.tenant_id=:tenant_id
GROUP BY coalesce(am.new_id,ab.address_id),ab.branch_id
HAVING count(DISTINCT ab.delivery_fee)>1 OR count(DISTINCT coalesce(ab.neighborhood_id,-1))>1 OR count(DISTINCT ab.is_covered)>1;

DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM customer_merge_conflict WHERE merge_run_id=(SELECT id FROM merge_run))
 THEN RAISE EXCEPTION 'Conflictos de dirección detectados; revise la salida del precheck (la transacción y su auditoría temporal se revertirán)'; END IF;
END $$;

-- Fill only missing useful fields on canonical addresses.
INSERT INTO customer_merge_conflict(merge_run_id,tenant_id,conflict_type,entity_ids,details)
SELECT (SELECT id FROM merge_run),:tenant_id,'address_metadata',ARRAY[keep.id,old.id],
 jsonb_build_object('canonical',to_jsonb(keep),'duplicate',to_jsonb(old))
FROM address_map m JOIN address old ON old.id=m.old_id JOIN address keep ON keep.id=m.new_id
WHERE (nullif(btrim(keep.label),'') IS NOT NULL AND nullif(btrim(old.label),'') IS NOT NULL AND keep.label<>old.label)
   OR (nullif(btrim(keep.additional_info),'') IS NOT NULL AND nullif(btrim(old.additional_info),'') IS NOT NULL AND keep.additional_info<>old.additional_info)
   OR (nullif(btrim(keep.instructions),'') IS NOT NULL AND nullif(btrim(old.instructions),'') IS NOT NULL AND keep.instructions<>old.instructions)
   OR (keep.latitude IS NOT NULL AND old.latitude IS NOT NULL AND keep.latitude<>old.latitude)
   OR (keep.longitude IS NOT NULL AND old.longitude IS NOT NULL AND keep.longitude<>old.longitude);

UPDATE address keep SET
 label=coalesce(keep.label,old.label), additional_info=coalesce(keep.additional_info,old.additional_info),
 instructions=coalesce(keep.instructions,old.instructions), latitude=coalesce(keep.latitude,old.latitude),
 longitude=coalesce(keep.longitude,old.longitude), original_address=coalesce(keep.original_address,old.original_address),
 normalized_address=coalesce(keep.normalized_address,old.normalized_address), validated_at=coalesce(keep.validated_at,old.validated_at),
 validation_source=coalesce(keep.validation_source,old.validation_source), is_primary=keep.is_primary OR old.is_primary
FROM address_map m JOIN address old ON old.id=m.old_id WHERE keep.id=m.new_id;

UPDATE "order" x SET address_id=m.new_id FROM address_map m WHERE x.address_id=m.old_id;
UPDATE storefront_checkout x SET saved_address_id=m.new_id FROM address_map m WHERE x.saved_address_id=m.old_id;
DELETE FROM address_branch old USING address_map m,address_branch keep
 WHERE old.address_id=m.old_id AND keep.address_id=m.new_id AND keep.tenant_id=old.tenant_id AND keep.branch_id=old.branch_id;
UPDATE address_branch x SET address_id=m.new_id FROM address_map m WHERE x.address_id=m.old_id;
DELETE FROM address x USING address_map m WHERE x.id=m.old_id;

-- Consolidate phone ownership before deactivating absorbed customers.
DELETE FROM customer_phone p USING (
 SELECT p.id,row_number() OVER(
   PARTITION BY p.tenant_id,coalesce(m.new_id,p.customer_id),p.phone_normalized
   ORDER BY (m.new_id IS NULL) DESC,p.is_primary DESC,p.created_at,p.id) rn
 FROM customer_phone p LEFT JOIN merge_map m ON m.old_id=p.customer_id
 WHERE p.tenant_id=:tenant_id
) duplicate WHERE p.id=duplicate.id AND duplicate.rn>1;
UPDATE customer_phone x SET customer_id=m.new_id,is_primary=false,updated_at=now() FROM merge_map m WHERE x.customer_id=m.old_id;

-- Keep the survivor's valid primary; otherwise use its oldest active phone.
UPDATE customer_phone p SET is_primary=false,updated_at=now()
WHERE p.tenant_id=:tenant_id AND p.customer_id IN(SELECT DISTINCT new_id FROM merge_map);
WITH preferred AS (
 SELECT p.id,row_number() OVER(PARTITION BY p.customer_id
   ORDER BY (p.phone_normalized=c.phone1) DESC,p.created_at,p.id) rn
 FROM customer_phone p JOIN customer c ON c.id=p.customer_id
 WHERE p.tenant_id=:tenant_id AND p.active AND p.customer_id IN(SELECT DISTINCT new_id FROM merge_map)
)
UPDATE customer_phone p SET is_primary=true,updated_at=now() FROM preferred x WHERE p.id=x.id AND x.rn=1;

-- Keep legacy phone columns synchronized only when the survivor already has mobile identity.
-- A 604 landline remains contact-only and is never promoted into CustomerPhone identity.
UPDATE customer c SET
 phone1=CASE WHEN regexp_replace(coalesce(c.phone1,''),'[^0-9]','','g') ~ '^604[0-9]{7}$' THEN c.phone1 ELSE phones.primary_phone END,
 phone2=CASE WHEN regexp_replace(coalesce(c.phone2,''),'[^0-9]','','g') ~ '^604[0-9]{7}$' THEN c.phone2 ELSE phones.secondary_phone END,
 name=CASE WHEN nullif(btrim(c.name),'') IS NULL THEN coalesce((
   SELECT nullif(btrim(old.name),'') FROM merge_map m JOIN customer old ON old.id=m.old_id
   WHERE m.new_id=c.id AND nullif(btrim(old.name),'') IS NOT NULL ORDER BY old.created_at,old.id LIMIT 1),c.name) ELSE c.name END,
 whatsapp_user_id=coalesce(c.whatsapp_user_id,(
   SELECT nullif(btrim(old.whatsapp_user_id),'') FROM merge_map m JOIN customer old ON old.id=m.old_id
   WHERE m.new_id=c.id AND nullif(btrim(old.whatsapp_user_id),'') IS NOT NULL ORDER BY old.created_at,old.id LIMIT 1)),
 whatsapp_username=coalesce(c.whatsapp_username,(
   SELECT nullif(btrim(old.whatsapp_username),'') FROM merge_map m JOIN customer old ON old.id=m.old_id
   WHERE m.new_id=c.id AND nullif(btrim(old.whatsapp_username),'') IS NOT NULL ORDER BY old.created_at,old.id LIMIT 1)),
 updated_at=now()
FROM (
 SELECT customer_id,
   (array_agg(phone_normalized ORDER BY is_primary DESC,created_at,id) FILTER(WHERE active))[1] primary_phone,
   (array_agg(phone_normalized ORDER BY is_primary DESC,created_at,id) FILTER(WHERE active))[2] secondary_phone
 FROM customer_phone WHERE tenant_id=:tenant_id GROUP BY customer_id
 ) phones
WHERE c.id=phones.customer_id AND c.id IN(SELECT DISTINCT new_id FROM merge_map);
UPDATE customer old SET active=false,updated_at=now() FROM merge_map m WHERE old.id=m.old_id;

UPDATE customer_merge_run SET status='completed',completed_at=now(),summary=jsonb_build_object(
 'customers_merged',(SELECT count(*) FROM merge_map),'addresses_merged',(SELECT count(*) FROM address_map))
WHERE id=(SELECT id FROM merge_run);
COMMIT;
