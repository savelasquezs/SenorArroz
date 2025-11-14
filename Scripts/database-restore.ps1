# ==============================================================================
# Script: Restaurar Base de Datos en Docker
# Propósito: Restaurar salida.sql y aplicar sincronización de esquema
# Fecha: Noviembre 2024
# ==============================================================================

param(
    [switch]$SkipSeed = $false,
    [string]$ContainerName = "senorarroz-postgres",
    [string]$DatabaseName = "senor_arroz",
    [string]$User = "postgres",
    [string]$Password = "1234"
)

$ErrorActionPreference = "Stop"

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Restauración de Base de Datos - SenorArroz API" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# Verificar que Docker está corriendo
Write-Host "Verificando que Docker está corriendo..." -ForegroundColor Yellow
$dockerRunning = docker ps 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error: Docker no está corriendo o no está disponible" -ForegroundColor Red
    exit 1
}

# Verificar que el contenedor existe
Write-Host "Verificando contenedor PostgreSQL..." -ForegroundColor Yellow
$containerExists = docker ps -a --filter "name=$ContainerName" --format "{{.Names}}"
if (-not $containerExists) {
    Write-Host "❌ Error: Contenedor '$ContainerName' no existe" -ForegroundColor Red
    Write-Host "   Asegúrate de que docker-compose esté corriendo:" -ForegroundColor Yellow
    Write-Host "   docker compose up -d" -ForegroundColor Cyan
    exit 1
}

# Verificar que el contenedor está corriendo
$containerRunning = docker ps --filter "name=$ContainerName" --format "{{.Names}}"
if (-not $containerRunning) {
    Write-Host "⚠️  Contenedor existe pero no está corriendo. Iniciando..." -ForegroundColor Yellow
    docker start $ContainerName
    Start-Sleep -Seconds 5
}

Write-Host "✅ Contenedor PostgreSQL verificado" -ForegroundColor Green
Write-Host ""

# Verificar que salida.sql existe
$salidaSql = "salida.sql"
if (-not (Test-Path $salidaSql)) {
    Write-Host "❌ Error: No se encontró el archivo '$salidaSql'" -ForegroundColor Red
    Write-Host "   Asegúrate de que el archivo esté en la raíz del proyecto" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ Archivo salida.sql encontrado" -ForegroundColor Green
Write-Host ""

# Paso 1: Restaurar salida.sql
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Paso 1: Restaurando salida.sql..." -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

$env:PGPASSWORD = $Password
$restoreCommand = "docker exec -i $ContainerName psql -U $User -d $DatabaseName < $salidaSql"

Write-Host "Ejecutando restauración (esto puede tardar varios minutos)..." -ForegroundColor Yellow
Get-Content $salidaSql | docker exec -i $ContainerName psql -U $User -d $DatabaseName

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error al restaurar salida.sql" -ForegroundColor Red
    exit 1
}

Write-Host "✅ salida.sql restaurado exitosamente" -ForegroundColor Green
Write-Host ""

# Paso 2: Aplicar sincronización de esquema
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Paso 2: Aplicando sincronización de esquema..." -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

$syncScript = "Scripts\sync-schema-to-ef.sql"
if (-not (Test-Path $syncScript)) {
    Write-Host "⚠️  Advertencia: No se encontró $syncScript" -ForegroundColor Yellow
    Write-Host "   Continuando sin sincronización..." -ForegroundColor Yellow
}
else {
    Write-Host "Aplicando sync-schema-to-ef.sql..." -ForegroundColor Yellow
    Get-Content $syncScript | docker exec -i $ContainerName psql -U $User -d $DatabaseName

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Error al aplicar sincronización" -ForegroundColor Red
        exit 1
    }

    Write-Host "✅ Sincronización de esquema aplicada exitosamente" -ForegroundColor Green
    Write-Host ""
}

# Paso 3: Aplicar datos iniciales (opcional)
if (-not $SkipSeed) {
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host "Paso 3: Aplicando datos iniciales..." -ForegroundColor Cyan
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host ""

    $seedScript = "Scripts\seed-data.sql"
    if (-not (Test-Path $seedScript)) {
        Write-Host "⚠️  Advertencia: No se encontró $seedScript" -ForegroundColor Yellow
        Write-Host "   Continuando sin datos iniciales..." -ForegroundColor Yellow
    }
    else {
        Write-Host "Aplicando seed-data.sql..." -ForegroundColor Yellow
        Get-Content $seedScript | docker exec -i $ContainerName psql -U $User -d $DatabaseName

        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Error al aplicar datos iniciales" -ForegroundColor Red
            exit 1
        }

        Write-Host "✅ Datos iniciales aplicados exitosamente" -ForegroundColor Green
        Write-Host ""
    }
}
else {
    Write-Host "⏭️  Saltando datos iniciales (--SkipSeed especificado)" -ForegroundColor Yellow
    Write-Host ""
}

# Verificación final
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "Verificación Final" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Verificando columnas y tablas sincronizadas..." -ForegroundColor Yellow

$verificationQuery = @"
SELECT 
    CASE 
        WHEN EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'address' AND column_name = 'is_primary') 
        THEN '✅ is_primary en address'
        ELSE '❌ is_primary en address FALTA'
    END as address_check,
    CASE 
        WHEN EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'order' AND column_name = 'guestname') 
        THEN '✅ guestname en order'
        ELSE '❌ guestname en order FALTA'
    END as order_check,
    CASE 
        WHEN EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'bank_payment' AND column_name = 'verified_at') 
        THEN '✅ verified_at en bank_payment'
        ELSE '❌ verified_at en bank_payment FALTA'
    END as bank_payment_check,
    CASE 
        WHEN EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'password_reset_token') 
        THEN '✅ password_reset_token existe'
        ELSE '❌ password_reset_token FALTA'
    END as password_reset_token_check;
"@

$verificationQuery | docker exec -i $ContainerName psql -U $User -d $DatabaseName -t

Write-Host ""
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "✅ Restauración completada exitosamente" -ForegroundColor Green
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Próximos pasos:" -ForegroundColor Yellow
Write-Host "  1. Verifica que los usuarios puedan hacer login" -ForegroundColor Cyan
Write-Host "  2. Si usaste seed-data.sql, asegúrate de reemplazar el hash de Luis1234" -ForegroundColor Cyan
Write-Host "  3. Prueba la API en http://localhost:5000/swagger" -ForegroundColor Cyan
Write-Host ""

