# Pruebas aisladas de negocio Rappi

Estas pruebas **no usan el portal Rappi ni Santander**. No equivalen a homologación del proveedor.

## Qué se ejecuta realmente

`SenorArroz.Tests/RappiBusinessFlowPostgreSqlTests.cs` combina PostgreSQL real con:

- `RappiOrderProcessor`, `OrderRepository` e `InventoryService` reales.
- `PrintQueueService` real: persiste la cola, pero su transporte es un doble; no imprime.
- `ChangeOrderStatusHandler` y el despacho READY del worker reales, invocados de forma acotada.
- Un slice HTTP con `TestServer`, el controller Rappi real y dependencias explícitas. El HTTP recibe el webhook; después otro scope procesa el inbox durable.

El proveedor Rappi es un mock **Strict**: una operación no preparada falla, en lugar de salir a Internet. Notificaciones, rutas y mapeo del DTO usan dobles; no se envía SignalR/FCM ni se crean rutas externas. Los productos, clientes, pagos, sucursales, tenants y secretos HMAC son sintéticos.

`Support/RappiIsolatedBusinessDatabase.cs` crea una base nueva por caso y la elimina al terminar. Si se suministra un servidor, solo admite `localhost`, `127.0.0.1` o `::1`; nunca inicializa la base indicada en la cadena. Esta barrera **no detecta un túnel a producción**: usar exclusivamente un PostgreSQL desechable local, jamás un port-forward de una base real.

## Repetición segura

Requisitos: .NET 9, dependencias NuGet restauradas y Docker local disponible. Desde la raíz del repositorio:

```bash
TEST_HOME="$(mktemp -d)"
PACKAGE_CACHE="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
env -i PATH="$PATH" HOME="$TEST_HOME" DOTNET_CLI_HOME="$TEST_HOME" \
  NUGET_PACKAGES="$PACKAGE_CACHE" DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  RUN_POSTGRES_INTEGRATION_TESTS=true \
  dotnet test SenorArroz.Tests/SenorArroz.Tests.csproj -c Release --no-restore \
  --filter 'FullyQualifiedName~RappiBusinessFlowPostgreSqlTests' \
  --logger 'trx;LogFileName=rappi-business-flow.trx'
```

Sin cadena suministrada, el fixture levanta `postgres:16-alpine` mediante Testcontainers. Si no hay Docker, se puede añadir al comando anterior una cadena **del servidor efímero local que se acaba de iniciar**, por ejemplo:

```text
RAPPI_POSTGRES_TEST_CONNECTION=Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;Pooling=false
```

Debe ir como un único argumento entre comillas a `env`; no usar cadenas o credenciales de Railway/Neon/Rappi. El usuario local necesita permiso de crear/eliminar las bases temporales. La bandera `RUN_POSTGRES_INTEGRATION_TESTS=true` es necesaria: un resultado **skipped no prueba nada**. Con `--no-build`, comprobar que el binario corresponde exactamente al árbol que se está revisando.

## Casos y aserciones

| Caso | Evidencia local |
|---|---|
| Growth no cero y duplicado | Persiste descuento total 3000, Rappi 2000 y partner 1000; un pedido, pago 16000, reserva estricta y trabajo de impresión; una aceptación simulada. |
| Taken → Ready repetido | Consume una reserva una sola vez, descuenta existencias, imprime una sola vez y completa un único envío READY simulado. |
| Aceptación 503 y consulta de eventos no disponible | Revierte pedido, pago, inventario y cola de impresión; conserva el error externo para revisión/reintento. |
| Dos sucursales y dos tenants sintéticos | Cada conexión usa su sucursal/app; el mismo ID del proveedor no colisiona entre conexiones; los filtros EF ocultan los registros del otro tenant. |
| Tienda de otra sucursal | Retiene el pedido sin llamar al proveedor ni reservar inventario o crear pagos. |
| NEW y CANCEL concurrentes, en ambos órdenes | Con conexiones PostgreSQL independientes y barreras deterministas: si aceptación gana, cancelación espera y luego revierte pago/libera reserva; si cancelación gana, el NEW superpuesto no llama al proveedor ni crea negocio. |
| Slice HTTP → inbox → negocio | Firma alterada: 401; endpoint administrativo anónimo: 401; admin sintético de otra sucursal: 403. Un webhook válido duplicado crea un solo inbox, y un scope posterior crea el negocio una sola vez. |

## Límites deliberados

- El servidor HTTP se construye desde cero: **no ejecuta `Program`, `WebApplicationFactory`, configuración de producción, factories Npgsql de la aplicación ni hosted workers**. Solo se registran las dependencias necesarias y se comprueba que no exista un `IDbContextFactory<ApplicationDbContext>` heredado.
- La autenticación HTTP es un esquema sintético, no validación de JWT real. El fixture valida filtros de tenant de EF y transacciones PostgreSQL; **no carga políticas RLS**. Las pruebas RLS existentes deben ejecutarse por separado cuando se evalúe esa capa.
- No verifica transporte de impresión, notificación de cocina, temporización del scheduler, conectividad/contratos reales de Rappi, validez de credenciales o aceptación de homologación.
- Un READY remoto exitoso seguido de fallo antes del commit local sigue requiriendo idempotencia/reconciliación del proveedor; estas pruebas no prometen entrega remota exactamente una vez.
- Los casos transaccionales de cancelación, liquidación concurrente y recuperación de efectos poscommit están en `RappiCancellationLifecycleTests`; se ejecutan con su fixture PostgreSQL existente, no con datos del negocio real.

Para cualquier siguiente prueba contra la plataforma, preparar primero conexiones, sucursales y base de datos exclusivamente de prueba. Un ambiente Rappi DEV que apunta al negocio real **no es aislamiento**.
