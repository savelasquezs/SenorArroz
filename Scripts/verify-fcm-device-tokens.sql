-- Verificación: tokens FCM guardados vía POST /api/deliverymen/me/device-token
-- Tabla: user_device_token (ver UserDeviceTokenConfiguration)

-- 1) Listado con usuario (vista operativa; el token completo es largo — solo preview)
SELECT
  t.id,
  t.user_id,
  u.name AS user_name,
  u.email,
  u.role,
  u.branch_id,
  u.active AS user_active,
  t.platform,
  LEFT(t.token, 32) || '…' AS token_preview,
  LENGTH(t.token) AS token_length,
  t.last_seen_at,
  t.created_at,
  t.updated_at
FROM user_device_token t
JOIN "user" u ON u.id = t.user_id
ORDER BY t.last_seen_at DESC NULLS LAST;

-- 2) Conteo por rol
SELECT u.role, COUNT(*) AS token_rows
FROM user_device_token t
JOIN "user" u ON u.id = t.user_id
GROUP BY u.role
ORDER BY token_rows DESC;

-- 3) Domiciliarios con al menos un token (quienes pueden recibir push de “pedido listo”)
SELECT u.id, u.name, u.branch_id, u.active, COUNT(t.id) AS device_count
FROM "user" u
JOIN user_device_token t ON t.user_id = u.id
WHERE u.role = 'deliveryman'
GROUP BY u.id, u.name, u.branch_id, u.active
ORDER BY u.name;

-- 4) Domiciliarios activos sin ningún token (no recibirán FCM)
SELECT u.id, u.name, u.branch_id
FROM "user" u
WHERE u.role = 'deliveryman'
  AND u.active = true
  AND NOT EXISTS (
    SELECT 1 FROM user_device_token t WHERE t.user_id = u.id
  )
ORDER BY u.branch_id, u.name;
