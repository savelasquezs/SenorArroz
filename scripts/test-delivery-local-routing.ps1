param([ValidateSet(3,5)][int]$Couriers=3,[ValidateRange(1,180)][int]$AgeMinutes=20,
    [Parameter(Mandatory)][string]$TestPassword)
$ErrorActionPreference='Stop'
Get-Content (Join-Path $PSScriptRoot 'set-delivery-local-scenario.sql') |
    docker exec -i senorarroz-delivery-postgres psql -U delivery_owner -d senor_arroz_delivery_dev -v courier_count=$Couriers -v age_minutes=$AgeMinutes | Out-Null
if($LASTEXITCODE -ne 0){throw 'No se pudo preparar el escenario local.'}
$login=Invoke-RestMethod 'http://127.0.0.1:5000/api/auth/login' -Method Post -ContentType 'application/json' `
    -Headers @{'X-Senor-Arroz-Client'='web'} -Body (@{email='admin@pruebas.local';password=$TestPassword}|ConvertTo-Json)
$headers=@{Authorization="Bearer $($login.token)";'X-Senor-Arroz-Client'='web'}
$plan=Invoke-RestMethod 'http://127.0.0.1:5000/api/delivery-routing/recalculate' -Method Post -Headers $headers
$ids=@($plan.proposals.stops.orderId)
if($plan.capacity.availableNow -ne $Couriers){throw 'La capacidad GPS no coincide con el escenario.'}
if(($ids|Select-Object -Unique).Count -ne $ids.Count){throw 'Un pedido aparece en varias rutas.'}
if($ids.Count+@($plan.unroutedOrders).Count -ne 20){throw 'El escenario debe conservar los 20 pedidos.'}
$report=[pscustomobject]@{couriers=$Couriers;ageMinutes=$AgeMinutes;version=$plan.version;
    included=$ids.Count;total=20;routes=@($plan.proposals | ForEach-Object {
        [pscustomobject]@{route=$_.sequence;orders=@($_.stops.orderId);drivingMinutes=[math]::Round($_.approximateDrivingDurationSeconds/60,1);
            lastArrivalMinutes=[math]::Round($_.lastDeliverySeconds/60,1)}
    });pending=@($plan.unroutedOrders | Select-Object orderId,address,unroutedReason)}
$directory=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/delivery-local'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$plan|ConvertTo-Json -Depth 30|Out-File (Join-Path $directory "scenario-$Couriers-couriers-$AgeMinutes-minutes-plan.json") -Encoding utf8
$report|ConvertTo-Json -Depth 10|Out-File (Join-Path $directory "scenario-$Couriers-couriers-$AgeMinutes-minutes.json") -Encoding utf8
$report|ConvertTo-Json -Depth 10
