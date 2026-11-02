# Control de costos de Google Routes

## Motivo

Google facturó consultas del SKU `Routes: Compute Routes Pro` durante octubre de 2026.
La implementación compartida usaba `TRAFFIC_AWARE_OPTIMAL` incluso para calcular
distancias de domicilios en la tienda web y para operaciones de rutina, haciendo
que todas esas llamadas se facturaran como Pro.

Fuente oficial (reglas de SKU): https://developers.google.com/maps/documentation/routes/usage-and-billing

## Política de llamadas

| Consumidor | Uso | Configuración |
| --- | --- | --- |
| `StorefrontCommerceService` | cobertura, precio y tiempo estimados del domicilio | Essentials (`TRAFFIC_UNAWARE`) |
| `DeliveryRouteWorkflowService` | métricas de la ruta asignada | Essentials |
| `DeliverymanAvailabilityService` | tiempo estimado de regreso | Essentials |
| `DeliveryRoutingPlanService` | validar propuestas de OR-Tools | Essentials por defecto; Pro solo con opt-in |

La opción de configuración `DeliveryRouting:UseTrafficAwareGoogleValidation`
es `false` por defecto. Activarla mediante
`DeliveryRouting__UseTrafficAwareGoogleValidation=true` habilita
`TRAFFIC_AWARE_OPTIMAL` **únicamente** durante las validaciones de propuestas
del planificador. No habilitarla sin justificar el uso de tráfico y establecer
límites de consumo.

Para impedir que una ruta larga se facture automáticamente como Pro, la
implementación de Essentials divide trayectos de más de 10 paradas
intermedias en varias consultas consecutivas. Suma distancias y duraciones;
la última sección aporta el tramo de regreso. No une las polilíneas de varias
respuestas: para esos trayectos la geometría completa no estará disponible.

## Verificación después de desplegar

1. Ejecutar pruebas backend y verificar que ninguna cotización web falla.
2. Comprobar una cotización de domicilio en cada sucursal, con una dirección
   de cobertura válida, sin modificar la selección automática de sucursal.
3. Verificar que las nuevas llamadas en Google Cloud Billing se imputan al
   SKU `Routes: Compute Routes Essentials`, no `Pro`. Hay retardo entre
   el uso y los informes de facturación.
4. Revisar nuevas solicitudes del optimizador solo si se habilitó explícitamente
   el flag Pro, y presupuestos/cuotas a nivel de Cloud Billing.
5. No deshabilitar Routes API: cotizaciones web y cálculo de distancias dependen
   de ella. Tampoco deshabilitar Places/Geocoding con este cambio.

No hay migraciones SQL ni cambios a la clave Google Maps. La tarifa final
depende de la cuota gratuita y del consumo real, no puede garantizarse
un cargo de $0 en meses futuros.
