# Script para generar hash BCrypt de Luis1234
# Ejecutar después de compilar el proyecto

Write-Host "Generando hash BCrypt para Luis1234..." -ForegroundColor Yellow

# Intentar cargar el assembly de BCrypt
$bcryptPath = "SenorArroz.API\bin\Debug\net9.0\BCrypt.Net-Next.dll"

if (Test-Path $bcryptPath) {
    try {
        Add-Type -Path $bcryptPath
        $hash = [BCrypt.Net.BCrypt]::HashPassword("Luis1234", 12)
        Write-Host ""
        Write-Host "Hash generado exitosamente:" -ForegroundColor Green
        Write-Host $hash -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Copia este hash y reemplázalo en Scripts/seed-data.sql" -ForegroundColor Yellow
        Write-Host "Busca: `$2a`$12`$PLACEHOLDER_FOR_LUIS1234_HASH_REPLACE_THIS" -ForegroundColor Yellow
    }
    catch {
        Write-Host "Error al generar hash: $_" -ForegroundColor Red
        Write-Host ""
        Write-Host "Alternativa: Usa un generador online de BCrypt" -ForegroundColor Yellow
        Write-Host "https://bcrypt-generator.com/ (usar rounds: 12)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "No se encontró el assembly de BCrypt en: $bcryptPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Primero compila el proyecto:" -ForegroundColor Yellow
    Write-Host "  dotnet build" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "O usa un generador online de BCrypt:" -ForegroundColor Yellow
    Write-Host "https://bcrypt-generator.com/ (usar rounds: 12, password: Luis1234)" -ForegroundColor Yellow
}

