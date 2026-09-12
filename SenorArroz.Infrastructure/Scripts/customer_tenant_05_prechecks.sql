\echo 'UNSUPPORTED CUSTOMER CONTACT PHONES (mobile and 604 fixed lines are valid)'
SELECT c.tenant_id, c.id customer_id, v.phone
FROM customer c CROSS JOIN LATERAL unnest(ARRAY[c.phone1,c.phone2]) v(phone)
WHERE nullif(trim(v.phone),'') IS NOT NULL
  AND (CASE WHEN length(regexp_replace(v.phone,'[^0-9]','','g'))=12 AND regexp_replace(v.phone,'[^0-9]','','g') LIKE '57%'
            THEN substring(regexp_replace(v.phone,'[^0-9]','','g') FROM 3)
            ELSE regexp_replace(v.phone,'[^0-9]','','g') END) !~ '^(3[0-9]{9}|604[0-9]{7})$';

\echo 'ADMINISTRATIVE 604 FIXED LINES (informational; excluded from CustomerPhone identity)'
SELECT c.tenant_id, count(*) phone_count, count(DISTINCT c.id) customer_count
FROM customer c CROSS JOIN LATERAL unnest(ARRAY[c.phone1,c.phone2]) v(phone)
WHERE (CASE WHEN length(regexp_replace(v.phone,'[^0-9]','','g'))=12 AND regexp_replace(v.phone,'[^0-9]','','g') LIKE '57%'
            THEN substring(regexp_replace(v.phone,'[^0-9]','','g') FROM 3)
            ELSE regexp_replace(v.phone,'[^0-9]','','g') END) ~ '^604[0-9]{7}$'
GROUP BY c.tenant_id;

\echo 'DUPLICATE PHONES AND AFFECTED CUSTOMERS'
SELECT tenant_id, phone_normalized, array_agg(DISTINCT customer_id ORDER BY customer_id) customer_ids,
       count(DISTINCT customer_id) customer_count
FROM customer_phone WHERE active
GROUP BY tenant_id, phone_normalized HAVING count(DISTINCT customer_id) > 1
ORDER BY tenant_id, phone_normalized;

\echo 'CONNECTED GROUPS, PROPOSED SURVIVOR AND REASSIGNMENT COUNTS'
WITH RECURSIVE edges AS (
  SELECT DISTINCT a.tenant_id, a.customer_id a, b.customer_id b
  FROM customer_phone a JOIN customer_phone b USING (tenant_id, phone_normalized)
  WHERE a.active AND b.active AND a.customer_id <> b.customer_id
), reach(tenant_id, root, member) AS (
  SELECT tenant_id, a, a FROM edges UNION SELECT tenant_id, a, b FROM edges
  UNION SELECT r.tenant_id, r.root, e.b FROM reach r JOIN edges e ON e.tenant_id=r.tenant_id AND e.a=r.member
), groups AS (
  SELECT tenant_id, member, min(root) group_id FROM reach GROUP BY tenant_id, member
), ranked AS (
  SELECT g.*, c.created_at,
    count(o.id) FILTER (WHERE o.status NOT IN ('cancelled','awaiting_payment')) valid_orders,
    row_number() OVER (PARTITION BY g.tenant_id,g.group_id ORDER BY count(o.id) FILTER (WHERE o.status NOT IN ('cancelled','awaiting_payment')) DESC,c.created_at,c.id) rn
  FROM groups g JOIN customer c ON c.id=g.member LEFT JOIN "order" o ON o.customer_id=c.id
  GROUP BY g.tenant_id,g.group_id,g.member,c.created_at,c.id
)
SELECT r.tenant_id,r.group_id,max(r.member) FILTER(WHERE rn=1) survivor_id,
       array_agg(r.member ORDER BY r.member) customer_ids,sum(valid_orders) valid_orders,
       sum((SELECT count(*) FROM "order" o WHERE o.customer_id=r.member)) orders_to_review,
       sum((SELECT count(*) FROM address a WHERE a.customer_id=r.member)) addresses_to_review,
       sum((SELECT count(*) FROM whatsapp_conversation w WHERE w.customer_id=r.member)) conversations_to_review,
       sum((SELECT count(*) FROM whatsapp_commerce_session w WHERE w.customer_id=r.member)) commerce_sessions_to_review,
       sum((SELECT count(*) FROM storefront_checkout s WHERE s.customer_id=r.member)) checkouts_to_review,
       sum((SELECT count(*) FROM delivery_app_connection d WHERE d.customer_id=r.member)) delivery_connections_to_review
FROM ranked r GROUP BY r.tenant_id,r.group_id ORDER BY r.tenant_id,r.group_id;

\echo 'TENANT INCONSISTENCIES'
SELECT 'customer/branch' relation, c.id child_id FROM customer c JOIN branch b ON b.id=c.branch_id WHERE c.tenant_id<>b.tenant_id
UNION ALL SELECT 'address/customer',a.id FROM address a JOIN customer c ON c.id=a.customer_id WHERE a.tenant_id<>c.tenant_id
UNION ALL SELECT 'neighborhood/branch',n.id FROM neighborhood n JOIN branch b ON b.id=n.branch_id WHERE n.tenant_id<>b.tenant_id
UNION ALL SELECT 'order/branch',o.id FROM "order" o JOIN branch b ON b.id=o.branch_id WHERE o.tenant_id<>b.tenant_id
UNION ALL SELECT 'order/customer',o.id FROM "order" o JOIN customer c ON c.id=o.customer_id WHERE o.tenant_id<>c.tenant_id;

\echo 'ORPHAN REFERENCES'
SELECT 'customer/branch' relation,c.id child_id FROM customer c LEFT JOIN branch b ON b.id=c.branch_id WHERE b.id IS NULL
UNION ALL SELECT 'address/customer',a.id FROM address a LEFT JOIN customer c ON c.id=a.customer_id WHERE c.id IS NULL
UNION ALL SELECT 'neighborhood/branch',n.id FROM neighborhood n LEFT JOIN branch b ON b.id=n.branch_id WHERE b.id IS NULL
UNION ALL SELECT 'order/customer',o.id FROM "order" o LEFT JOIN customer c ON c.id=o.customer_id WHERE o.customer_id IS NOT NULL AND c.id IS NULL
UNION ALL SELECT 'order/branch',o.id FROM "order" o LEFT JOIN branch b ON b.id=o.branch_id WHERE b.id IS NULL;

\echo 'ADDRESS-BRANCH DUPLICATES OR CROSS-TENANT ROWS'
SELECT tenant_id,address_id,branch_id,count(*) FROM address_branch GROUP BY 1,2,3 HAVING count(*)>1;
SELECT ab.id FROM address_branch ab JOIN address a ON a.id=ab.address_id JOIN branch b ON b.id=ab.branch_id
WHERE ab.tenant_id<>a.tenant_id OR ab.tenant_id<>b.tenant_id;

\echo 'CONFLICTING SERVICE DATA FOR EQUIVALENT PHYSICAL ADDRESSES'
WITH n AS (
 SELECT a.id,a.tenant_id,a.customer_id,
 customer_address_normalize(coalesce(nullif(a.normalized_address,''),nullif(a.original_address,''),a.address)) key
 FROM address a
)
SELECT n.tenant_id,n.customer_id,n.key,ab.branch_id,array_agg(DISTINCT ab.address_id) address_ids,
 array_agg(DISTINCT ab.delivery_fee) fees,array_agg(DISTINCT ab.neighborhood_id) neighborhoods,array_agg(DISTINCT ab.is_covered) coverage
FROM n JOIN address_branch ab ON ab.address_id=n.id AND ab.tenant_id=n.tenant_id
GROUP BY n.tenant_id,n.customer_id,n.key,ab.branch_id
HAVING count(DISTINCT ab.delivery_fee)>1 OR count(DISTINCT coalesce(ab.neighborhood_id,-1))>1 OR count(DISTINCT ab.is_covered)>1;

\echo 'ALL FOREIGN KEYS REFERENCING CUSTOMER (compare with merge script allow-list)'
SELECT conrelid::regclass AS referencing_table, a.attname AS referencing_column, conname
FROM pg_constraint c
JOIN unnest(c.conkey) WITH ORDINALITY k(attnum,ord) ON true
JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum
WHERE c.contype='f' AND c.confrelid='customer'::regclass ORDER BY 1,2;
