\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE requested_addresses(address text, neighborhood text, latitude numeric, longitude numeric) ON COMMIT DROP;
INSERT INTO requested_addresses VALUES
('Calle 78 #79a-3','Robledo Diamante',6.280336,-75.585504),
('Carrera 68 94-97','Castilla',6.289412,-75.572831),
('Cr 72 #95 107','Castilla',6.291668,-75.575081),
('hospital Pablo Tobón','Robledo Cordoba',6.276844,-75.579704),
('Cra67#106b-4','Gratamira',6.303879,-75.566611),
('Calle 95#79 a 32','Kennedy',6.294037,-75.583867),
('calle 103 # 65 34','Girardot',6.297360,-75.567386),
('Cll 114a 63 D 38','Playitas-Toscana',6.309520,-75.559221),
('Calle 94 # 74 a 27','Castilla',6.290600,-75.579075),
('Parque Girardota','Girardota',6.377581,-75.446200),
('Calle 115 #75a12','Florencia',6.310454,-75.570210),
('Calle 77 c 86a46','Robledo Bello Horizonte',6.283697,-75.590655),
('Calle 104 a #80-12','12 de octubre',6.304766,-75.577928),
('Calle 89 b # 71 b 28','Alfonso lopez',6.284402,-75.578441),
('Cra90d 76-20','Robledo Aures',6.284744,-75.595465),
('Cra.78b#107b42','Santander',6.307922,-75.574961),
('Calle 110 #75-18','Florencia',6.308429,-75.571026),
('Carrera 75# 92-211','Kennedy',6.290265,-75.580011),
('Transversal 103b # 74-74','Pedregal',6.301263,-75.573958),
('Crr 74b #93-83','Castilla',6.290427,-75.579431);

INSERT INTO address(tenant_id,customer_id,address,latitude,longitude,normalized_address,original_address,validation_source,created_at,updated_at,delivery_fee)
SELECT 1,1,r.address,r.latitude,r.longitude,r.address,r.address,'local_coordinates_test',now(),now(),0
FROM requested_addresses r
WHERE NOT EXISTS (SELECT 1 FROM address a WHERE a.tenant_id=1 AND a.address=r.address);

WITH targets AS (
  SELECT id, row_number() OVER (ORDER BY id) AS rn
  FROM "order"
  WHERE tenant_id=1 AND delivery_man_id IS NULL AND delivery_route_id IS NULL AND notes LIKE 'Prueba real%'
), addresses AS (
  SELECT a.id, row_number() OVER (ORDER BY r.address) AS rn
  FROM requested_addresses r JOIN address a ON a.tenant_id=1 AND a.address=r.address
)
UPDATE "order" o
SET address_id=a.id, customer_id=1, status=CASE WHEN t.rn IN (4,8,12,16) THEN 'in_preparation' ELSE 'ready' END,
    status_times=CASE WHEN t.rn IN (4,8,12,16)
      THEN jsonb_build_object('taken',now()-interval '10 minutes','inpreparation',now()-interval '5 minutes')
      ELSE jsonb_build_object('taken',now()-interval '12 minutes','inpreparation',now()-interval '10 minutes','ready',now()-interval '1 minute') END,
    created_at=now()-interval '20 minutes', updated_at=now(), notes='Prueba coordenadas 2026-10-18 #'||t.rn
FROM targets t JOIN addresses a ON a.rn=t.rn WHERE o.id=t.id AND t.rn <= 19;

DO $$ DECLARE admin_id integer; product_id integer; product_price integer; aid integer; BEGIN
  SELECT id INTO admin_id FROM "user" WHERE email='admin@pruebas.local';
  SELECT id,price INTO product_id,product_price FROM product WHERE tenant_id=1 AND active ORDER BY id LIMIT 1;
  SELECT a.id INTO aid FROM address a WHERE a.tenant_id=1 AND a.address='Crr 74b #93-83' LIMIT 1;
  INSERT INTO "order"(tenant_id,branch_id,taken_by_id,customer_id,address_id,type,status,status_times,subtotal,total,delivery_fee,created_at,updated_at,notes)
  VALUES(1,1,admin_id,1,aid,'delivery','ready',jsonb_build_object('taken',now()-interval '12 minutes','inpreparation',now()-interval '10 minutes','ready',now()-interval '1 minute'),product_price,product_price,0,now()-interval '20 minutes',now(),'Prueba coordenadas 2026-10-18 #20');
END $$;
COMMIT;
