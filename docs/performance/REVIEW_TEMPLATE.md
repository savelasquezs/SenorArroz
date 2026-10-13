# Plantilla de auditoría diaria (copiar a reviews/YYYY-MM-DD-Mxx-Fyy.md)

> Auditoría de **solo lectura**. No alterar datos, código ni despliegues sin aprobación explícita.

- **Fecha:** YYYY-MM-DD
- **Feature:** Mxx-Fyy — título
- **Estado previo / nuevo:** Pendiente → Analizado
- **Prioridad:** Alta / Media / Baja (motivo)
- **Repositorios / ramas / commits:** API (...), Vue (...), Flutter (...), Next.js (...), Print Agent (...)
- **Roles y sucursales afectados:** ...

## 1. Flujo real
- Acción de usuario, vista/componente y navegación:
- Llamadas API (método, ruta, evento que las dispara, cantidad y payload aproximado):
- Controller → Handler/Service → Repository/DbContext → tablas/índices:
- Realtime, workers, tareas periódicas o proveedores externos vinculados:
- Restricciones de autorización/tenant/estado:

## 2. Línea base comprobable
| Métrica | Valor actual | Procedimiento/fuente | Entorno | Observaciones |
|---|---|---|---|---|
| HTTP requests por interacción | Sin medir | | | |
| Latencia HTTP p50/p95 | Sin medir | | | |
| Consultas SQL por interacción | Sin medir | | | |
| Tiempo SQL y plan EXPLAIN (sin ANALYZE sobre producción sin evaluar impacto) | Sin medir | | | |
| Respuesta / egress por interacción | Sin medir | | | |
| RAM/CPU | Sin medir | | | |
| Errores/retries | Sin medir | | | |

No usar números inventados. Etiquetar cualquier estimación como tal. No publicar secretos ni datos personales.

## 3. Hallazgos y diagnóstico
| Evidencia (archivo/línea, log, consulta) | Problema demostrado o hipótesis | Impacto | Confianza |
|---|---|---|---|
| | | | |

## 4. Opciones de mejora
| Propuesta | Impacto esperado | Riesgo funcional / seguridad | Complejidad | Tests necesarios |
|---|---|---|---|---|
| | | | |

**Recomendación para decisión del usuario:** ...
**Decisión del usuario, fecha y alcance aprobado:** Pendiente.

## 5. Implementación (solo después de aprobar)
- Issue y PR:
- Pruebas ejecutadas:
- Deploy y entorno:
- Rollback:
- Métricas después / comparación y conclusión:
- Estado final del checklist:

## 6. Siguiente revisión sugerida
- Feature:
- Motivo:
