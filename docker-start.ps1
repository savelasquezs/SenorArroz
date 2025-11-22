# Script de inicio rápido para Docker - SenorArroz API (Modo Desarrollo)
# Ejecuta este script en PowerShell para iniciar la aplicación con hot reload

Write-Host "🐳 Iniciando SenorArroz API con Docker (Modo Desarrollo)..." -ForegroundColor Cyan
Write-Host ""

# Cargar variables de entorno del frontend
$frontendEnvPath = Join-Path $PSScriptRoot "..\senorArrozFront\.env"
if (Test-Path $frontendEnvPath) {
    Write-Host "📝 Cargando variables de entorno del frontend..." -ForegroundColor Yellow
    Get-Content $frontendEnvPath | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]*)=(.*)$') {
            $key = $matches[1].Trim()
            $value = $matches[2].Trim()
            # Remover comillas si existen (dobles o simples)
            if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                $value = $value.Trim('"')
            }
            if ($value.StartsWith("'") -and $value.EndsWith("'")) {
                $value = $value.Trim("'")
            }
            [Environment]::SetEnvironmentVariable($key, $value, "Process")
        }
    }
    Write-Host "✅ Variables de entorno cargadas" -ForegroundColor Green
} else {
    Write-Host "⚠️  No se encontró .env en el frontend. Asegúrate de tener VITE_GOOGLE_MAPS_API_KEY configurada." -ForegroundColor Yellow
}

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
Write-Host "Construyendo imágenes Docker (modo desarrollo con hot reload)..." -ForegroundColor Yellow
docker compose -f docker-compose.yml -f docker-compose.dev.yml build

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error al construir las imágenes" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Iniciando contenedores..." -ForegroundColor Yellow
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error al iniciar los contenedores" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Esperando a que los servicios estén listos..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

Write-Host ""
Write-Host "✅ ¡Aplicación iniciada exitosamente en modo desarrollo!" -ForegroundColor Green
Write-Host ""
Write-Host "📍 URLs disponibles:" -ForegroundColor Cyan
Write-Host "   - API Swagger: http://localhost:5000" -ForegroundColor White
Write-Host "   - Frontend: http://localhost:5174" -ForegroundColor White
Write-Host "   - PostgreSQL: localhost:5433" -ForegroundColor White
Write-Host ""
Write-Host "🔄 Hot Reload activado:" -ForegroundColor Cyan
Write-Host "   - Los cambios en el backend se reflejarán automáticamente" -ForegroundColor White
Write-Host "   - Los cambios en el frontend se reflejarán automáticamente" -ForegroundColor White
Write-Host ""
Write-Host "📊 Comandos útiles:" -ForegroundColor Cyan
Write-Host "   - Ver logs: docker compose -f docker-compose.yml -f docker-compose.dev.yml logs -f" -ForegroundColor White
Write-Host "   - Detener: .\docker-stop.ps1" -ForegroundColor White
Write-Host "   - Estado: docker compose -f docker-compose.yml -f docker-compose.dev.yml ps" -ForegroundColor White
Write-Host ""
Write-Host "Abriendo Swagger en el navegador..." -ForegroundColor Yellow
Start-Process "http://localhost:5000"

