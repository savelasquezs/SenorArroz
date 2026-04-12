-- ============================================================
-- Script para actualizar el token FCM en la BD LOCAL de prueba
-- Ejecutar una vez después de obtener el token real del dispositivo
-- (ver logcat: línea "FCM_TOKEN_FULL: ...")
-- ============================================================

-- 1) Reemplaza '<PEGA_AQUI_EL_TOKEN_COMPLETO>' con el valor de FCM_TOKEN_FULL del logcat.

-- Ver qué hay actualmente
SELECT u.name, u.id, t.id AS token_id, LEFT(t.token, 40) || '…' AS token_preview, t.platform, t.last_seen_at
FROM user_device_token t
JOIN "user" u ON u.id = t.user_id
WHERE u.role = 'deliveryman' AND u.active = true
ORDER BY t.last_seen_at DESC NULLS LAST;

-- 2) Actualizar (o insertar) el token para el domiciliario de prueba.
--    Ajusta user_id según quién es el "domiciliario libre" en tu BD local
--    (normalmente el id 3 = Abelardo, branch_id = 1).

-- OPCIÓN A: actualizar el token existente
UPDATE user_device_token
SET token         = '<PEGA_AQUI_EL_TOKEN_COMPLETO>',
    platform      = 'android',
    last_seen_at  = NOW(),
    updated_at    = NOW()
WHERE user_id = 3;  -- ajusta si el id local es diferente

-- OPCIÓN B: insertar si no existe ningún registro
-- INSERT INTO user_device_token (user_id, token, platform, created_at, updated_at, last_seen_at)
-- SELECT 3, '<PEGA_AQUI_EL_TOKEN_COMPLETO>', 'android', NOW(), NOW(), NOW()
-- WHERE NOT EXISTS (SELECT 1 FROM user_device_token WHERE user_id = 3);

-- 3) Verificar
SELECT u.name, LEFT(t.token, 40) || '…' AS token_preview, t.last_seen_at
FROM user_device_token t
JOIN "user" u ON u.id = t.user_id
WHERE u.id = 3;
