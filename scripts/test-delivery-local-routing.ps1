param([ValidateSet(3,5)][int]$Couriers=3,[ValidateSet(1,2,3)][int]$Package=1,[ValidateRange(1,120)][int]$AgeMinutes=12,
    [Parameter(Mandatory)][string]$TestPassword)
$ErrorActionPreference='Stop'
$packets=Get-Content (Join-Path $PSScriptRoot 'delivery-routing-addresses.json') -Raw | ConvertFrom-Json -NoEnumerate
$addresses=ConvertTo-Json -InputObject $packets[$Package-1] -Depth 10 -Compress
Get-Content (Join-Path $PSScriptRoot 'set-delivery-local-scenario.sql') |
    docker exec -i senorarroz-delivery-postgres psql -U delivery_owner -d senor_arroz_delivery_dev -v courier_count=$Couriers -v age_minutes=$AgeMinutes -v address_data=$addresses | Out-Null
if($LASTEXITCODE -ne 0){throw 'No se pudo preparar el escenario local.'}
$login=Invoke-RestMethod 'http://127.0.0.1:5000/api/auth/login' -Method Post -ContentType 'application/json' `
    -Headers @{'X-Senor-Arroz-Client'='web'} -Body (@{email='admin@pruebas.local';password=$TestPassword}|ConvertTo-Json)
$headers=@{Authorization="Bearer $($login.token)";'X-Senor-Arroz-Client'='web'}
$plan=Invoke-RestMethod 'http://127.0.0.1:5000/api/delivery-routing/recalculate' -Method Post -Headers $headers
$ids=@($plan.proposals.stops.orderId)
if($plan.capacity.availableNow -ne $Couriers){throw 'La capacidad GPS no coincide con el escenario.'}
if(($ids|Select-Object -Unique).Count -ne $ids.Count){throw 'Un pedido aparece en varias rutas.'}
if($ids.Count+@($plan.unroutedOrders).Count -ne 20){throw 'El escenario debe conservar los 20 pedidos.'}
if($ids -notcontains 18 -or @($plan.proposals)[0].stops.orderId -notcontains 18){throw 'El más antiguo debe salir y priorizar su propuesta.'}
if($Package -eq 1 -and $Couriers -eq 3 -and @(18..27|Where-Object {$_ -notin $ids}).Count){throw 'La combinación conocida de los diez más antiguos no debe perderse.'}
$allStops=@($plan.proposals.stops)+@($plan.unroutedOrders)
if(@($allStops|Where-Object {-not $_.isReady -or $_.reservedForUtc}).Count){throw 'Esta prueba exige todos listos y sin reservas.'}
$chronological=@($allStops|Sort-Object orderId)
for($index=1;$index -lt $chronological.Count;$index++) {
    if([datetime]$chronological[$index].createdAtUtc -le [datetime]$chronological[$index-1].createdAtUtc){throw 'Las horas no respetan la antigüedad de los pedidos.'}
}
foreach($proposal in $plan.proposals) {
    foreach($stop in $proposal.stops) {
        $seconds=([datetime]$stop.estimatedArrivalAtUtc-[datetime]$proposal.expectedDepartureAtUtc).TotalSeconds
        $limit=if($stop.deliveryTimeLimitSeconds){$stop.deliveryTimeLimitSeconds}else{2400}
        if($seconds -gt $limit){throw "El pedido $($stop.orderId) excede su límite."}
    }
}
$reloaded=Invoke-RestMethod 'http://127.0.0.1:5000/api/delivery-routing/plan' -Headers $headers
if($reloaded.version -ne $plan.version -or @($reloaded.proposals.stops.orderId).Count -ne $ids.Count){throw 'La recarga cambió el plan sin cambios operativos.'}
$report=[pscustomobject]@{package=$Package;couriers=$Couriers;ageMinutes=$AgeMinutes;version=$plan.version;solverDurationMs=$plan.solverDurationMs;
    included=$ids.Count;total=20;routes=@($plan.proposals | ForEach-Object {
        $departure=[datetime]$_.expectedDepartureAtUtc
        [pscustomobject]@{route=$_.sequence;orders=@($_.stops.orderId);drivingMinutes=[math]::Round($_.approximateDrivingDurationSeconds/60,1);
            lastArrivalMinutes=[math]::Round($_.lastDeliverySeconds/60,1);
            stops=@($_.stops|ForEach-Object {[pscustomobject]@{orderId=$_.orderId;address=$_.address;neighborhood=$_.neighborhood;
                arrivalMinutes=[math]::Round(([datetime]$_.estimatedArrivalAtUtc-$departure).TotalMinutes,1);
                ageMinutes=[math]::Round(([datetime]$plan.generatedAtUtc-[datetime]$_.createdAtUtc).TotalMinutes,1);
                exceptionLimitSeconds=$_.deliveryTimeLimitSeconds}})}
    });pending=@($plan.unroutedOrders | Select-Object orderId,address,neighborhood,unroutedReason)}
$directory=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/delivery-local'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$plan|ConvertTo-Json -Depth 30|Out-File (Join-Path $directory "refactor-package-$Package-$Couriers-couriers-plan.json") -Encoding utf8
$report|ConvertTo-Json -Depth 10|Out-File (Join-Path $directory "refactor-package-$Package-$Couriers-couriers.json") -Encoding utf8
$report|ConvertTo-Json -Depth 10
