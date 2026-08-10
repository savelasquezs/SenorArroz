# Enrutador dinámico V1

## Flujo

1. `GET /api/delivery-routing/plan` toma los pedidos elegibles de la sucursal efectiva.
2. El estimador de cocina calcula disponibilidad esperada con historia reciente y fallback configurable.
3. La disponibilidad GPS produce vehículos abstractos libres ahora o próximos.
4. `ApproximateRoutingCostMatrixProvider` calcula costos locales sin Google Route Matrix.
5. `OrToolsDeliveryRouteOptimizer` asigna y ordena visitas opcionales sin máximo fijo de paradas.
6. Google Compute Routes valida únicamente las propuestas finales configuradas.
7. Web y Flutter reciben el plan y sus cambios por SignalR.
8. `POST /api/orders/delivery/self-assign` reclama los pedidos Listos con `proposalId` y `expectedPlanVersion`.

## Configuración

La sección `DeliveryRouting` controla activación, tiempos de espera, límite del solver, frescura GPS, estimación de preparación, velocidad/factor vial, penalizaciones, finalistas de Google y segundos de servicio por pedido.

`ShadowMode` queda disponible para rollout controlado. La selección manual y el flujo real `DeliveryRoute` permanecen operativos.

## Persistencia y despliegue

Ejecutar `SenorArroz.Infrastructure/Scripts/add_delivery_routing_v1.sql` antes del backend. El script es idempotente y crea planes, propuestas, paradas, relaciones e índices.

Las tablas son aisladas por `branch_id` durante la fase actual. Cuando exista `Tenant`, deben recibir `tenant_id` desde la sucursal y activar filtros globales según `MULTITENANT_PLAN.md`.

## Degradación

- Sin coordenadas de sucursal: no se crean propuestas y el plan lo advierte.
- Sin coordenadas de pedido: el pedido aparece en `unroutedOrders` con `requiresLocation`.
- Sin capacidad: aparece con `noCapacity`.
- Fallo de Google: se conservan métricas aproximadas y `googleValidationStatus=degraded`.
- Carrera al reclamar: HTTP 409 con código `ROUTING_PLAN_STALE`; el cliente refresca el plan.
