# Script de inicio rápido para Docker - SenorArroz API
# Ejecuta este script en PowerShell para iniciar la aplicación

Write-Host "🐳 Iniciando SenorArroz API con Docker..." -ForegroundColor Cyan
Write-Host ""

# Verificar que Docker esté corriendo
Write-Host "Verificando Docker Desktop..." -ForegroundColor Yellow
try {
    docker ps | Out-Null
    Write-Host "✅ Docker está corriendo" -ForegroundColor Green
} catch {
    Write-Host "❌ Error: Docker Desktop no está corriendo. Por favor inicia Docker Desktop primero." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Construyendo imágenes Docker (esto puede tardar varios minutos la primera vez)..." -ForegroundColor Yellow
docker compose build

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error al construir las imágenes" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Iniciando contenedores..." -ForegroundColor Yellow
docker compose up -d

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error al iniciar los contenedores" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Esperando a que los servicios estén listos..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

Write-Host ""
Write-Host "✅ ¡Aplicación iniciada exitosamente!" -ForegroundColor Green
Write-Host ""
Write-Host "📍 URLs disponibles:" -ForegroundColor Cyan
Write-Host "   - API Swagger: http://localhost:5000" -ForegroundColor White
Write-Host "   - PostgreSQL: localhost:5432" -ForegroundColor White
Write-Host ""
Write-Host "📊 Comandos útiles:" -ForegroundColor Cyan
Write-Host "   - Ver logs: docker compose logs -f" -ForegroundColor White
Write-Host "   - Detener: docker compose down" -ForegroundColor White
Write-Host "   - Estado: docker compose ps" -ForegroundColor White
Write-Host ""
Write-Host "Abriendo Swagger en el navegador..." -ForegroundColor Yellow
Start-Process "http://localhost:5000"

