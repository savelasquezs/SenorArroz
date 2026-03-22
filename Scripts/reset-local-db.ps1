# Reinicia la BD local y aplica local-init-completo.sql
# Ejecutar desde la carpeta SenorArroz\:
#   .\Scripts\reset-local-db.ps1
#
# Variables opcionales:
#   $env:PGPASSWORD = '...'
#   $env:PGDATABASE_NAME = 'senorArroz'

$ErrorActionPreference = 'Stop'

$HostName = if ($env:PGHOST) { $env:PGHOST } else { 'localhost' }
$Port = if ($env:PGPORT) { $env:PGPORT } else { '5433' }
$PgUser = if ($env:PGUSER) { $env:PGUSER } else { 'postgres' }
$Db = if ($env:PGDATABASE_NAME) { $env:PGDATABASE_NAME } else { 'senorArroz' }
if (-not $env:PGPASSWORD) { $env:PGPASSWORD = 'Santy1994.' }

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SqlFile = Join-Path $ScriptDir 'local-init-completo.sql'

$env:PGPASSWORD = $env:PGPASSWORD

Write-Host ">>> Cortando conexiones a `"$Db`"..."
& psql -h $HostName -p $Port -U $PgUser -d postgres -v ON_ERROR_STOP=1 -c @"
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$Db' AND pid <> pg_backend_pid();
"@ 2>$null

Write-Host ">>> DROP DATABASE IF EXISTS `"$Db`"..."
& psql -h $HostName -p $Port -U $PgUser -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS `"$Db`";"

Write-Host ">>> CREATE DATABASE `"$Db`"..."
& psql -h $HostName -p $Port -U $PgUser -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE `"$Db`";"

Write-Host ">>> Aplicando $SqlFile..."
& psql -h $HostName -p $Port -U $PgUser -d $Db -v ON_ERROR_STOP=1 -f $SqlFile

Write-Host ">>> Listo. Base `"$Db`" reconstruida."
