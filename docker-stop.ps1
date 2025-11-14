# Script para detener los contenedores Docker
# Ejecuta este script en PowerShell

Write-Host "🛑 Deteniendo contenedores Docker..." -ForegroundColor Yellow
docker compose down

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Contenedores detenidos exitosamente" -ForegroundColor Green
} else {
    Write-Host "❌ Error al detener los contenedores" -ForegroundColor Red
}

Write-Host ""
Write-Host "💡 Para eliminar también los volúmenes (y borrar la base de datos), ejecuta:" -ForegroundColor Cyan
Write-Host "   docker compose down -v" -ForegroundColor White

