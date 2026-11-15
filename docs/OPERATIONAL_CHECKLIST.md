# Recordatorios operativos

## Alcance

Un checklist compartido por sucursal. Dos franjas: 10:30 y 18:00 America/Bogota; miércoles sin tareas. Los condicionales cuentan en el badge. Ninguna tarea bloquea la operación. Permisos de lectura y marcación: Superadmin, Admin, Cashier. Configuración: Superadmin y Admin dentro de su sucursal.

## Persistencia y concurrencia

Dos tablas: configuración y estado. El estado conserva una sola fila por tarea, sin quién marcó ni historial. Se interpreta como desmarcado si su fecha no es hoy en Colombia. Cada escritura envía el valor deseado, la fecha y revisión; nunca un toggle. Solicitudes de ayer y cambios concurrentes se rechazan con 409. La notificación reutiliza OrderHub y el grupo Admin de tenant/sucursal.

El segundo pedido de carne aparece jueves/viernes con una condición visible; no se infiere una compra ni se inventa un control semanal. El lunes festivo se desplaza al martes con calendario nacional determinista.

## Activación segura

1. Aplicar `SenorArroz.Infrastructure/Scripts/add_operational_checklist.sql` en el entorno de destino. Es aditivo e idempotente. Cargar las definiciones operativas por un canal privado: nunca incluir textos internos del manual en este repositorio público. Seguir AGENTS.md para Railway.
2. Desplegar backend y luego frontend. No publicar código dependiente del esquema antes del SQL.
3. Configurar en el servidor, nunca en el navegador: `ReminderDocumentation__TenantId`, `ReminderDocumentation__GitHubToken`, `ReminderDocumentation__Repository`, `ReminderDocumentation__Branch`. Usar token de contenido con acceso únicamente al repositorio del manual. No incluirlo en logs ni commits.
4. El publicador solo escribe `snippets/operational-checklist.mdx`, con marcador de archivo generado. Nunca reemplaza páginas manuales. Importar el snippet en la página Programación de Mintlify después de conciliar/publicar los borradores pendientes.
5. Guardar una tarea en Configurar recordatorios o pulsar Sincronizar. Una publicación fallida NO revierte la configuración del POS; queda indicada para reintento. `synced_revision` evita dar por publicada una revisión posterior. Un commit enviado no equivale a despliegue de Mintlify finalizado.

No se necesitan workers, cron, nuevas librerías UI ni servicios externos de tareas. La importación en el manual es:

```mdx
import OperationalChecklist from '/snippets/operational-checklist.mdx';

<OperationalChecklist />
```

## API

- GET `/api/operational-checklist/today`: día local, horarios UTC y tareas.
- GET `/api/operational-checklist/configuration`: configuración paginada de la sucursal.
- PUT `/api/operational-checklist/configuration`: alta/edición/desactivación; publicación de tablas.
- PUT `/api/operational-checklist/{id}/state`: marcación explícita compartida.
- POST `/api/operational-checklist/sync-mintlify`: reintento de publicación.

El contexto tenant proviene de la sesión. El contexto de sucursal reutiliza IBranchContext/X-Branch-Id y nunca se acepta un tenant del frontend.
