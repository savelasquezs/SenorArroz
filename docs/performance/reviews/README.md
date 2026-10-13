# Informes diarios de auditoría y corrección

Cada funcionalidad revisada se documenta como `YYYY-MM-DD-Mxx-Fyy.md` con la [plantilla](../REVIEW_TEMPLATE.md), y se enlaza desde el [CHECKLIST.md](../CHECKLIST.md).

El procedimiento autorizado es **auditar → corregir en rama nueva → probar → abrir PR(s) hacia `main` → presentar al propietario**. No esperar autorización para preparar el código, las pruebas o scripts de PostgreSQL **fuera de producción**.

Si afecta varios repositorios, registrar SHA base de `main`, rama, commits, dependencias entre PRs, orden compatible de despliegue, riesgos y resultado real de las pruebas. Si no hay acceso a instrumentos de medición o a tests, declararlo y no afirmar que está listo para merge.

**El propietario revisará y hará merge; también ejecutará las migraciones necesarias en producción.** Nunca aplicar scripts ni modificar datos productivos, fusionar o desplegar automáticamente. El informe posterior al merge debe comprobar comportamiento, métricas y actualización de estado a `Implementado`/`Validado`.

No publicar secretos, logs con datos personales o credenciales. Usar PR documental para incluir informes y estados del checklist, incluso cuando la corrección se haga en otro repositorio.
