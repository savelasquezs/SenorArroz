# API Dashboard — convenciones y rutas



Los datos del dashboard **no** se exponen en un único endpoint. Cada vista del front tiene su recurso bajo el prefijo común **`/api/dashboard`**, con filtros propios.



## Autenticación y alcance por rol



- **JWT** obligatorio (`Authorization: Bearer …`).

- **Superadmin:** puede enviar `branchId` opcional en query. Si **omite** `branchId`, el agregado incluye **todas** las sucursales.

- **Admin (y resto con acceso):** el alcance es **siempre** la sucursal del usuario (`branch_id` del token). Cualquier `branchId` en query enviado por un no-superadmin **se ignora** (anti-fuga entre sucursales).



## Rutas



| Ruta | Vista front | Filtros query (resumen) |

|------|-------------|-------------------------|

| `GET /api/dashboard/main` | **Principal** | `branchId?`, `activityLimit?`, `kpiFrom?` + `kpiTo?` (ambas o ninguna) |

| `GET /api/dashboard/delivery` | **Domicilios** | **`from` y `to` obligatorios** (UTC, ISO 8601), `branchId?` |

| `GET /api/dashboard/sales` | **Ventas** (futuro) | `branchId?`, `fromDate?`, `toDate?`, … |



## `GET /api/dashboard/main`



**Roles:** `Admin`, `Superadmin`.



**Query:**



| Parámetro | Tipo | Descripción |

|-----------|------|-------------|

| `branchId` | int? | Solo superadmin. Filtra pedidos/KPIs/pipeline/actividad a esa sucursal. |

| `activityLimit` | int | Máximo de ítems en `recentActivity` (default 20, máx. 50). |

| `kpiFrom` | DateTime? | Inicio del rango **UTC** para KPIs comparativos. Debe ir junto con `kpiTo`. |

| `kpiTo` | DateTime? | Fin del rango **UTC** para KPIs. Debe ir junto con `kpiFrom`. |



Si **no** se envían `kpiFrom`/`kpiTo`, los KPIs usan ventanas rolling (7 días / 365 días) definidas en el handler. Si **sí** se envían ambas, se calculan comparaciones sobre ese rango, el periodo anterior contiguo y el mismo rango del año anterior.



**Respuesta (`200`):** ver DTOs en `SenorArroz.Application/Features/Dashboard/DTOs/`.



- **`kpis`:** métricas comparativas según el modo (rolling o rango explícito).

- **`pipeline`:** conteo de pedidos en estados operativos **en curso** (`taken`, `in_preparation`, `ready`, `on_the_way`), no histórico.

- **`recentActivity`:** últimas actualizaciones de pedidos (orden por `UpdatedAt`), texto orientado a UI.

- **`avgPrepMinutes` / `avgDeliveryMinutes`:** promedios (minutos) de preparación y entrega sobre pedidos **domicilio entregados** en la misma ventana que los KPI (`kpiFrom`–`kpiTo` o últimos 7 días), vía `StatusTimes` (misma lógica que `delivery`). Sin datos completos → `0`.



## `GET /api/dashboard/delivery`



**Roles:** `Admin`, `Superadmin`.



**Query:**



| Parámetro | Tipo | Descripción |

|-----------|------|-------------|

| `from` | DateTime | Inicio del rango (obligatorio). |

| `to` | DateTime | Fin del rango (obligatorio). |

| `branchId` | int? | Solo superadmin; mismo criterio de alcance que en `main`. |



El front suele enviar el día completo (inicio 00:00 y fin 23:59:59.999 en zona local, en ISO). El backend acota el rango máximo (~400 días).



**Respuesta (`200`):** `DashboardDeliveryResponseDto` — promedios de preparación y entrega, lista de repartidores agregada, series de evolución (etiquetas + entregas + fees por bucket según amplitud del rango).



## Arquitectura (CQRS)



- **Controller:** `DashboardController` → MediatR.

- **Handlers:** `GetDashboardMainHandler`, `GetDashboardDeliveryHandler` → `IOrderRepository` y agregadores en Application/Infrastructure.



## Errores



- **401 / 403:** según políticas de autorización estándar de la API.

