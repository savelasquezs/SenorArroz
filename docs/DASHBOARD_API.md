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

| `GET /api/dashboard/sales/comparison` | **Ventas** — comparativa sucursales | **`from` y `to` obligatorios**, `branchId?` |
| `GET /api/dashboard/sales/evolution` | **Ventas** — líneas tiempo | **`from` y `to` obligatorios**, `branchId?` |
| `GET /api/dashboard/sales/products` | **Ventas** — ranking + donut | **`from` y `to` obligatorios**, `branchId?`, `top` (5–20, default 10), `groupBy` (`product` \| `category`, default `product`) |
| `GET /api/dashboard/expenses/summary` | **Gastos** — KPIs | **`from` y `to` obligatorios**, `branchId?` |
| `GET /api/dashboard/expenses/by-category` | **Gastos** — torta por categoría | **`from` y `to` obligatorios**, `branchId?` |
| `GET /api/dashboard/expenses/timeseries` | **Gastos** — evolución | **`from` y `to` obligatorios**, `branchId?`, `categoryId?`, `expenseId?`, `granularity` (`day` \| `month`, vacío = auto) |



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



## `GET /api/dashboard/sales/comparison`

Pedidos **no cancelados** con `CreatedAt` en `[from, to]` (máx. ~400 días). Una fila por sucursal (todas o la de `branchId` en superadmin). Totales y desglose delivery vs resto (`Onsite` + `Reservation`). `deliveryTimeMinutes` reservado (hoy `0`).

**Respuesta:** `DashboardSalesComparisonResponseDto` (`rows[]`).



## `GET /api/dashboard/sales/evolution`

Mismas reglas de rango y alcance. Devuelve los **ocho bloques** que consume el front (`TimeEvolutionPanel`): ventas multi-sucursal y pedidos agregados por día, **hora del día UTC del `to`**, mes y año. Día: máx. 62 buckets; hora: 24 franjas `00:00`–`23:00`.

**Respuesta:** `DashboardSalesEvolutionResponseDto`.



## `GET /api/dashboard/sales/products`

Líneas de detalle de pedido en el rango (mismo criterio de pedidos). **Top** por cantidad vendida (`top` 5–20). **Participación:** top 5 por recaudo + slice **Otros** con % sobre el total del rango.

**Query `groupBy`:** `product` (default) agrupa por producto; `category` agrupa por categoría del producto. La forma de la respuesta es la misma: `topByQuantity[].id` es `productId` o `categoryId` según el modo.

**`weightByCategory`:** siempre por **categoría de producto** (independiente de `groupBy`). Lista de `{ categoryId, name, totalWeightGrams }` donde `totalWeightGrams` es la suma de `cantidad × weight_grams` del producto en cada línea de pedido, **solo** si el producto tiene `weight_grams` definido. Categorías con total 0 no se incluyen. *Futuro posible: cruzar con gastos por categoría para costo; aún no implementado.*

**Respuesta:** `DashboardSalesProductsResponseDto`.



## `GET /api/dashboard/expenses/summary`

Comprobantes de gasto (`ExpenseHeader`) con `CreatedAt` en `[from, to]` (máx. ~400 días). Importes desde líneas `ExpenseDetail` (`total` o `cantidad × amount`).

**Respuesta:** `DashboardExpenseSummaryResponseDto` — `totalCop`, `headerCount`, `lineCount`, `avgDailyCop`, `avgTicketCop`, `previousPeriodTotalCop`, `totalChangeFromPreviousPercent`, etc.



## `GET /api/dashboard/expenses/by-category`

Mismo rango y alcance. Suma por categoría del catálogo `Expense` para gráfico de torta.



## `GET /api/dashboard/expenses/timeseries`

Serie alineada al rango: sin `categoryId` ni `expenseId` = **total**; solo `categoryId` = total de esa categoría; `expenseId` = ese ítem (se ignora categoría inconsistente). `granularity` vacío: **día** si el rango ≤ 62 días, si no **mes**.



## Arquitectura (CQRS)



- **Controller:** `DashboardController` → MediatR.

- **Handlers:** además de ventas/principal/delivery, `GetDashboardExpenseSummaryHandler`, `GetDashboardExpenseByCategoryHandler`, `GetDashboardExpenseTimeSeriesHandler` → `IExpenseDashboardRepository`, `IExpenseRepository`, `IExpenseCategoryRepository`.



## Errores



- **401 / 403:** según políticas de autorización estándar de la API.

