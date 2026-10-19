param([switch]$SkipBuild,[switch]$SimulateRouting)
$ErrorActionPreference = 'Stop'
$apiRoot = Split-Path $PSScriptRoot -Parent
$apiProject = Join-Path $apiRoot 'SenorArroz.API'
$apiDll = Join-Path $apiProject 'bin/Debug/net9.0/SenorArroz.API.dll'
$runtimeDirectory = Join-Path $apiRoot 'artifacts/delivery-local'
New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null

$pidFile = Join-Path $runtimeDirectory 'api.pid'
if (Test-Path -LiteralPath $pidFile) {
    $previousPid = [int](Get-Content -LiteralPath $pidFile)
    $previous = Get-CimInstance Win32_Process -Filter "ProcessId=$previousPid"
    if ($previous -and $previous.CommandLine.Contains($apiDll)) { Stop-Process -Id $previousPid }
}

if (-not $SkipBuild) {
    dotnet build (Join-Path $apiProject 'SenorArroz.API.csproj') --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la API local.' }
}

$localSecrets = (dotnet user-secrets list --id senor-arroz-delivery-local --json | Where-Object { $_ -notmatch '^//' }) -join "`n" | ConvertFrom-Json
$connection = $localSecrets.ConnectionString
if ($connection -notmatch '^Host=127.0.0.1;Port=15433;Database=senor_arroz_delivery_dev;') { throw 'Falta la conexión de desarrollo local en User Secrets.' }
docker start senorarroz-delivery-postgres | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No está disponible PostgreSQL de desarrollo.' }
$env:ConnectionStrings__DefaultConnection = $connection
$env:JwtSettings__SecretKey = $localSecrets.JwtSecret
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5000'
$env:Cors__AllowedOrigins__9 = 'http://127.0.0.1:8088'
$env:Fcm__ProjectId = ''
$env:FirebaseStorage__Enabled = 'false'
$env:DeliveryRouting__Measurement__Enabled = 'false'
$env:DeliveryRouting__MaximumFinalistsToValidate = '0'
$env:GOOGLE_MAPS_GEOCODING_API_KEY = ''
$env:GOOGLE_MAPS_ROUTES_API_KEY = ''
$env:ApiPublic__BaseUrl = 'http://localhost:5000'
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Warning', 'Process')

docker stop senorarroz-api | Out-Null
$process = Start-Process dotnet -ArgumentList ('"' + $apiDll + '" --Fcm:ProjectId=') -WorkingDirectory $apiProject -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimeDirectory 'api.stdout.log') -RedirectStandardError (Join-Path $runtimeDirectory 'api.stderr.log')
$process.Id | Out-File -LiteralPath $pidFile -Encoding ascii
for ($attempt=0; $attempt -lt 30; $attempt++) {
    try {
        $null = Invoke-WebRequest 'http://127.0.0.1:5000/api/auth/delivery-app-version' -TimeoutSec 2
        if($SimulateRouting) {
            $simulationScript=Join-Path $PSScriptRoot 'simulate-delivery-local-gps.ps1'
            $simulationPidFile=Join-Path $runtimeDirectory 'gps-simulation.pid'
            if(Test-Path -LiteralPath $simulationPidFile) {
                $previousSimulation=Get-CimInstance Win32_Process -Filter "ProcessId=$([int](Get-Content -LiteralPath $simulationPidFile))"
                if($previousSimulation -and $previousSimulation.CommandLine.Contains($simulationScript)) {
                    Stop-Process -Id $previousSimulation.ProcessId
                }
            }
            $simulation=Start-Process (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-File',('"'+$simulationScript+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimeDirectory 'gps-simulation.stdout.log') -RedirectStandardError (Join-Path $runtimeDirectory 'gps-simulation.stderr.log')
            $simulation.Id | Out-File -LiteralPath $simulationPidFile -Encoding ascii
        }
        Write-Output "API local: http://127.0.0.1:5000 (PID $($process.Id)). Base: senor_arroz_delivery_dev."
        return
    } catch { Start-Sleep -Seconds 1 }
}
throw "La API no respondió. Revisa $runtimeDirectory."
