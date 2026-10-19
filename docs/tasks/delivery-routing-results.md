# Resultados del enrutamiento local — 18 de octubre de 2026

Base descartada y reconstruida desde `d03e6a9` (API) y `2a3fe72` (app). Pruebas completamente locales, sin push, PR ni despliegue. El usuario aprobó conservar esta primera versión mediante commits locales para las pruebas de los domiciliarios.

Todos los escenarios usan 20 pedidos listos, sin reservas, con edades de 50 a 12 minutos correlativas del #18 al #37. El ID identifica la fixture, no es una prioridad del algoritmo. Sucursal Santander: 6.308047, -75.574383. Radio operativo: 600 m.

Los tiempos son aproximados desde salida, incluyen servicio entre entregas y no equivalen al tiempo total desde toma. Meta 35 min, techo normal 40 min; Girardota tiene excepción automática propia. La matriz local no conoce cruces ni sentidos viales: no se afirma validación de Google.

Validación: backend completo 968 aprobadas, 26 omitidas, 0 fallidas (994 en total); Flutter 5 pruebas del modelo aprobadas, análisis de los 3 archivos modificados sin problemas y compilación web correcta. Se comprobó recarga del plan sin cambio de versión en los seis escenarios y las cinco rutas en el navegador, incluida la excepción de Girardota. Las rutas son propuestas, no se aceptaron ni asignaron manualmente.

| Paquete | Domiciliarios | Incluidos | Pendientes | Cálculo |
|---|---:|---:|---:|---:|
| 1 | 3 | 13/20 | 7 | 920 ms |
| 1 | 5 | 20/20 | 0 | 720 ms |
| 2 | 3 | 14/20 | 6 | 895 ms |
| 2 | 5 | 20/20 | 0 | 693 ms |
| 3 | 3 | 18/20 | 2 | 761 ms |
| 3 | 5 | 20/20 | 0 | 677 ms |

## Paquete 1 — 3 domiciliarios

### Ruta 1 — última entrega 39.2 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #31 | Calle 94 # 74 a 27 | Castilla | 24 min | 7.1 min |
| #26 | Hospital Pablo Tobón | Robledo Córdoba | 34 min | 16.6 min |
| #18 | METTAGRUP SAS | San Diego | 50 min | 39.2 min |
### Ruta 2 — última entrega 37.8 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #25 | Cr 72 #95 107 | Castilla | 36 min | 6.5 min |
| #19 | Kra 69a#93-110 | Castilla | 48 min | 11.8 min |
| #22 | Cra 73a #93-76 | Castilla | 42 min | 17.2 min |
| #20 | Carrera 74a #94-53 | Castilla | 46 min | 22 min |
| #28 | Calle 95#79 a 32 | Kennedy | 30 min | 28.3 min |
| #23 | Calle 78 #79a-3 | Robledo Diamante | 40 min | 37.8 min |
### Ruta 3 — última entrega 21.9 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #27 | Cra67#106b-4 | Gratamira | 32 min | 3.5 min |
| #29 | calle 103 # 65 34 | Girardot | 28 min | 10.1 min |
| #21 | Carrera 67#97-123 | Castilla | 44 min | 16.2 min |
| #24 | Carrera 68 94-97 | Castilla | 38 min | 21.9 min |

### Pendientes

- #30 · Cll 114a 63 D 38 · Playitas-Toscana: requiere una combinación en otra dirección.
- #32 · Parque Girardota · Girardota: requiere una combinación en otra dirección.
- #33 · Calle 115 #75a12 · Florencia: requiere una combinación en otra dirección.
- #34 · Calle 77 c 86a46 · Robledo Bello Horizonte: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #35 · Calle 104 a #80-12 · 12 de Octubre: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #36 · Calle 89 b # 71 b 28 · Alfonso López: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #37 · Cra90d 76-20 · Robledo Aures: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.

## Paquete 1 — 5 domiciliarios

### Ruta 1 — última entrega 39 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #21 | Carrera 67#97-123 | Castilla | 44 min | 6.1 min |
| #24 | Carrera 68 94-97 | Castilla | 38 min | 11.7 min |
| #18 | METTAGRUP SAS | San Diego | 50 min | 39 min |
### Ruta 2 — última entrega 37.4 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #35 | Calle 104 a #80-12 | 12 de Octubre | 16 min | 1.9 min |
| #25 | Cr 72 #95 107 | Castilla | 36 min | 11.2 min |
| #19 | Kra 69a#93-110 | Castilla | 48 min | 16.5 min |
| #22 | Cra 73a #93-76 | Castilla | 42 min | 22 min |
| #20 | Carrera 74a #94-53 | Castilla | 46 min | 26.7 min |
| #31 | Calle 94 # 74 a 27 | Castilla | 24 min | 31 min |
| #36 | Calle 89 b # 71 b 28 | Alfonso López | 14 min | 37.4 min |
### Ruta 3 — última entrega 39.6 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #28 | Calle 95#79 a 32 | Kennedy | 30 min | 6.7 min |
| #34 | Calle 77 c 86a46 | Robledo Bello Horizonte | 18 min | 15.6 min |
| #23 | Calle 78 #79a-3 | Robledo Diamante | 40 min | 22 min |
| #26 | Hospital Pablo Tobón | Robledo Córdoba | 34 min | 28.6 min |
| #37 | Cra90d 76-20 | Robledo Aures | 12 min | 39.6 min |
### Ruta 4 — última entrega 11.6 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #29 | calle 103 # 65 34 | Girardot | 28 min | 5 min |
| #27 | Cra67#106b-4 | Gratamira | 32 min | 11.6 min |
### Ruta 5 — última entrega 66 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #33 | Calle 115 #75a12 | Florencia | 20 min | 1.9 min |
| #30 | Cll 114a 63 D 38 | Playitas-Toscana | 26 min | 10.2 min |
| #32 | Parque Girardota | Girardota | 22 min | 66 min (excepción automática: límite 67.2 min) |

### Pendientes

Ninguno.

## Paquete 2 — 3 domiciliarios

### Ruta 1 — última entrega 39.3 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #30 | Calle 104 a #80-12 | 12 de Octubre | 26 min | 1.9 min |
| #20 | Cr 72 #95 107 | Castilla | 46 min | 11.2 min |
| #26 | Calle 94 # 74 a 27 | Castilla | 34 min | 16.8 min |
| #23 | Calle 95#79 a 32 | Kennedy | 40 min | 23.2 min |
| #18 | Calle 78 #79a-3 | Robledo Diamante | 50 min | 32.6 min |
| #21 | Hospital Pablo Tobón | Robledo Córdoba | 44 min | 39.3 min |
### Ruta 2 — última entrega 33.2 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #22 | Cra67#106b-4 | Gratamira | 42 min | 3.5 min |
| #24 | calle 103 # 65 34 | Girardot | 38 min | 10.1 min |
| #19 | Carrera 68 94-97 | Castilla | 48 min | 17.9 min |
| #36 | Transversal 103b # 74-74 | Pedregal | 14 min | 26.6 min |
| #33 | Cra.78b#107b42 | Santander | 20 min | 33.2 min |
### Ruta 3 — última entrega 66 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #28 | Calle 115 #75a12 | Florencia | 30 min | 1.9 min |
| #25 | Cll 114a 63 D 38 | Playitas-Toscana | 36 min | 10.2 min |
| #27 | Parque Girardota | Girardota | 32 min | 66 min (excepción automática: límite 67.2 min) |

### Pendientes

- #29 · Calle 77 c 86a46 · Robledo Bello Horizonte: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #31 · Calle 89 b # 71 b 28 · Alfonso López: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #32 · Cra90d 76-20 · Robledo Aures: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #34 · Calle 110 #75-18 · Florencia: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #35 · Carrera 75# 92-211 · Kennedy: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.
- #37 · Crr 74b #93-83 · Castilla: no encaja dentro de los límites de llegada y retraso de las salidas encontradas.

## Paquete 2 — 5 domiciliarios

### Ruta 1 — última entrega 29.8 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #23 | Calle 95#79 a 32 | Kennedy | 40 min | 6.7 min |
| #29 | Calle 77 c 86a46 | Robledo Bello Horizonte | 28 min | 15.6 min |
| #32 | Cra90d 76-20 | Robledo Aures | 22 min | 21.5 min |
| #18 | Calle 78 #79a-3 | Robledo Diamante | 50 min | 29.8 min |
### Ruta 2 — última entrega 36.7 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #36 | Transversal 103b # 74-74 | Pedregal | 14 min | 2.7 min |
| #24 | calle 103 # 65 34 | Girardot | 38 min | 9.7 min |
| #20 | Cr 72 #95 107 | Castilla | 46 min | 17.4 min |
| #19 | Carrera 68 94-97 | Castilla | 48 min | 22.7 min |
| #31 | Calle 89 b # 71 b 28 | Alfonso López | 24 min | 29.7 min |
| #21 | Hospital Pablo Tobón | Robledo Córdoba | 44 min | 36.7 min |
### Ruta 3 — última entrega 17.6 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #34 | Calle 110 #75-18 | Florencia | 18 min | 1.3 min |
| #25 | Cll 114a 63 D 38 | Playitas-Toscana | 36 min | 10 min |
| #22 | Cra67#106b-4 | Gratamira | 42 min | 17.6 min |
### Ruta 4 — última entrega 24.2 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #33 | Cra.78b#107b42 | Santander | 20 min | 0.2 min |
| #30 | Calle 104 a #80-12 | 12 de Octubre | 26 min | 6 min |
| #35 | Carrera 75# 92-211 | Kennedy | 16 min | 15.7 min |
| #37 | Crr 74b #93-83 | Castilla | 12 min | 20 min |
| #26 | Calle 94 # 74 a 27 | Castilla | 34 min | 24.2 min |
### Ruta 5 — última entrega 61.2 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #28 | Calle 115 #75a12 | Florencia | 30 min | 1.9 min |
| #27 | Parque Girardota | Girardota | 32 min | 61.2 min (excepción automática: límite 67.2 min) |

### Pendientes

Ninguno.

## Paquete 3 — 3 domiciliarios

### Ruta 1 — última entrega 36.4 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #18 | Transversal 103b # 74-74 | Pedregal | 50 min | 2.7 min |
| #21 | calle 103e # 67-65 | Girardot | 44 min | 8.7 min |
| #20 | calle 103-63e-113 | Belalcázar | 46 min | 14.9 min |
| #27 | carrera 67-100-46 | Castilla | 32 min | 20.7 min |
| #34 | Cra 68 #96-65 | Castilla | 18 min | 26.5 min |
| #23 | Carrera 65c # 93-51 | Castilla | 40 min | 32.2 min |
| #29 | Cr 66 93-71 | Castilla | 28 min | 36.4 min |
### Ruta 2 — última entrega 39.4 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #31 | Calle 104c cr 80c 21 | 12 de Octubre | 24 min | 1.8 min |
| #33 | Cll 98d #78a-19 | Castilla | 20 min | 9 min |
| #35 | Carrera 80 #95 a 17 primer piso | 12 de Octubre | 16 min | 14.6 min |
| #19 | Crr 74b #93-83 | Castilla | 48 min | 20.4 min |
| #26 | calle 93-73b-13 | Castilla | 34 min | 25.1 min |
| #30 | Carrera 70A. #93-14 | Castilla | 26 min | 30.5 min |
| #28 | Hospital Pablo Tobón Uribe | Robledo Córdoba | 30 min | 39.4 min |
### Ruta 3 — última entrega 23.4 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #32 | calle 108a # 77d-80 | Santander | 22 min | 0.1 min |
| #22 | Carrera 79 # 107 b - 14 | Santander | 42 min | 4.7 min |
| #25 | Carrera 75 # 20 AB 10 | París-Bello | 36 min | 11.1 min |
| #24 | calle 82 # 90e-75 | Robledo Villa Sofía | 38 min | 23.4 min |

### Pendientes

- #36 · Calle 115numero 76d23 · Santander: requiere una combinación en otra dirección.
- #37 · calle 116A 64D 75 -1 piso · Boyacá Las Brisas: requiere una combinación en otra dirección.

## Paquete 3 — 5 domiciliarios

### Ruta 1 — última entrega 31.4 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #21 | calle 103e # 67-65 | Girardot | 44 min | 3.8 min |
| #20 | calle 103-63e-113 | Belalcázar | 46 min | 10 min |
| #27 | carrera 67-100-46 | Castilla | 32 min | 15.8 min |
| #18 | Transversal 103b # 74-74 | Pedregal | 50 min | 22.6 min |
| #19 | Crr 74b #93-83 | Castilla | 48 min | 31.4 min |
### Ruta 2 — última entrega 30 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #22 | Carrera 79 # 107 b - 14 | Santander | 42 min | 0.6 min |
| #31 | Calle 104c cr 80c 21 | 12 de Octubre | 24 min | 5.9 min |
| #24 | calle 82 # 90e-75 | Robledo Villa Sofía | 38 min | 17.7 min |
| #25 | Carrera 75 # 20 AB 10 | París-Bello | 36 min | 30 min |
### Ruta 3 — última entrega 22.9 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #34 | Cra 68 #96-65 | Castilla | 18 min | 6.5 min |
| #23 | Carrera 65c # 93-51 | Castilla | 40 min | 12.3 min |
| #30 | Carrera 70A. #93-14 | Castilla | 26 min | 17.6 min |
| #29 | Cr 66 93-71 | Castilla | 28 min | 22.9 min |
### Ruta 4 — última entrega 25.6 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #33 | Cll 98d #78a-19 | Castilla | 20 min | 4.6 min |
| #35 | Carrera 80 #95 a 17 primer piso | 12 de Octubre | 16 min | 10.3 min |
| #26 | calle 93-73b-13 | Castilla | 34 min | 16.8 min |
| #28 | Hospital Pablo Tobón Uribe | Robledo Córdoba | 30 min | 25.6 min |
### Ruta 5 — última entrega 16.4 min

| Orden | Dirección | Barrio | Antigüedad al calcular | Llegada desde salida |
|---|---|---|---:|---:|
| #32 | calle 108a # 77d-80 | Santander | 22 min | 0.1 min |
| #37 | calle 116A 64D 75 -1 piso | Boyacá Las Brisas | 12 min | 8.5 min |
| #36 | Calle 115numero 76d23 | Santander | 14 min | 16.4 min |

### Pendientes

Ninguno.

## Estado final

API en http://127.0.0.1:5000; web en http://127.0.0.1:8088. Se dejó el primer paquete con 5 domiciliarios, 20 pedidos listos y las 5 propuestas. El arranque Android no se modificó y no se reinstaló la app del teléfono. El GPS simulado mantiene la capacidad local vigente.
