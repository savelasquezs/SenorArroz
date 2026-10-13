# Plantilla diaria — diagnóstico, corrección y PR (copiar a reviews/YYYY-MM-DD-Mxx-Fyy.md)

> Autorización permanente: analizar, preparar cambios en rama independiente, probar y abrir PR(s) a `main`. **No realizar merge, migración o despliegue productivo:** esos pasos quedan a cargo del propietario. Ver [README.md](README.md).

- **Fecha:** YYYY-MM-DD (America/Bogota)
- **ID / funcionalidad:** Mxx-Fyy — título
- **Estado propuesto:** Pendiente → Analizado / PR listo para revisión / Bloqueado
- **Prioridad:** Alta / Media / Baja (motivo operacional)
- **Repositorios implicados, ramas `main` base y SHA:** ...
- **Ramas creadas / commits / PRs hacia `main`:** ...
- **Roles, sucursales, tenants y flujos afectados:** ...
- **Ambiente de pruebas:** ...
- **Cambios en BD:** No aplica / Script preparado, aún sin ejecutar en producción

## 1. Flujo y causa raíz
- Acción de usuario y pantalla/componente/store/composable:
- Solicitudes HTTP: método, ruta, activador, cantidad medida, payload, refetch, deduplicación y caché:
- Controller → Handler/Service → Repository/DbContext → SQL/tablas/índices:
- Dependencias con SignalR, workers, Flutter/background, Next.js, Print Agent y servicios externos:
- Permisos de rol, sucursal, tenant y reglas de negocio:
- Reproducción del defecto y evidencia concreta (archivo/línea, trazas anonimizadas):
- **Hallazgos comprobados vs hipótesis no demostradas:**

## 2. Línea base antes de la corrección

| Métrica | Antes | Cómo se obtuvo | Entorno | Observaciones |
|---|---|---|---|---|
| Solicitudes HTTP por interacción | Sin medir | | | |
| Latencia HTTP p50/p95 | Sin medir | | | |
| Consultas SQL por interacción | Sin medir | | | |
| SQL / EXPLAIN / índices | Sin medir | | | |
| Bytes/respuesta y network egress | Sin medir | | | |
| RAM/CPU | Sin medir | | | |
| Reintentos, duplicados y errores | Sin medir | | | |

No inventar datos. Si no es posible medir, declarar limitaciones. No publicar secretos ni datos personales reales.

## 3. Corrección implementada en rama
- **Cambio mínimo, razón técnica y mecanismo de mejora:** ...
- **Archivos y lógica cambiados:** ...
- **Garantías de comportamiento:** funcionalidad, concurrencia, tenant/RLS, permisos, dinero/inventario, offline/tracking si aplica.
- **Alternativas consideradas y riesgos:** ...
- **Prueba de regresión que falla antes y pasa después (cuando sea posible):** ...
- **PR(s) enlazados por repo hacia `main`:** ...
- **CI, lint, build, pruebas unitarias/integración/E2E:** nombre, resultado y enlaces.
- **Limitaciones de verificación:** ... (si falta una prueba crítica, mantener PR en borrador / estado Bloqueado)

## 4. Comparación después del cambio

| Métrica | Antes | Después | Evidencia / entorno | Conclusión |
|---|---|---|---|---|
| Solicitudes HTTP | | | | |
| Latencia p50/p95 | | | | |
| Consultas / tiempo SQL | | | | |
| Red / egress | | | | |
| RAM / CPU | | | | |
| Errores y reintentos | | | | |

Distinguir medición en pruebas de validación en producción, todavía pendiente.

## 5. Base de datos: ejecutar solo después de aprobación
- **¿Hay migración?:** No / Sí.
- **Nombre de script SQL idempotente versionado:** `SenorArroz.Infrastructure/Scripts/...`
- **Tablas/columnas/índices afectados, tenant y RLS:** ...
- **Validación en PostgreSQL de prueba, reejecución y compatibilidad:** ...
- **Backfill / impacto en datos existentes:** ...
- **Precondiciones / backup y plan de reversión:** ...
- **Orden seguro de aplicación y desplegado:** ... (si la app depende de nuevo esquema, preparar SQL antes del despliegue compatible).
- **Comandos exactos para que el propietario aplique en Railway:** ... (revisados y seguros).
- **Estado en producción:** Pendiente. Nunca afirmar aplicado sin verificación posterior.

## 6. Entrega para revisión
- **Diagnóstico resumido:** ...
- **PR(s) a revisar:** ...
- **Veredicto:** PR listo para revisión / PR borrador por bloqueos / Sin cambio necesario y justificado.
- **Checklist:** estado propuesto, informe vinculado y PRs; no marcar Implementado ni Validado antes del merge/deploy y medición pertinente.
- **Acciones reservadas al propietario:** revisar y fusionar PRs; aplicar migración si corresponde; ejecutar o autorizar despliegue; confirmar.
- **Cómo verificar tras fusionar y criterios de rollback:** ...
- **Siguiente feature recomendado:** ...

El informe y el estado del checklist deben versionarse también mediante un PR documental si afectan a un repositorio distinto. Se permite preparar estos PRs sin aprobación adicional; no se permite fusionarlos automáticamente.
