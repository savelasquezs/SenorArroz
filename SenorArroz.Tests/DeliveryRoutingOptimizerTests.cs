using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class DeliveryRoutingOptimizerTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompassSectorsTreatNorthAsACircularBoundary()
    {
        Assert.Equal(0, RoutingDirectionHelper.Sector(350));
        Assert.Equal(0, RoutingDirectionHelper.Sector(10));
        Assert.Equal(20, RoutingDirectionHelper.Separation(350,10));
        Assert.True(RoutingDirectionHelper.CanShare(0,350,0,10,90));
        Assert.False(RoutingDirectionHelper.CanShare(0,10,4,170,90));
    }

    [Fact]
    public void CorridorCostPrefersProgressAlongTheSameDirection()
    {
        Assert.Equal(0, RoutingDirectionHelper.CorridorTransitionCost(180,180,1000,2000,1000));
        Assert.True(RoutingDirectionHelper.CorridorTransitionCost(180,220,1000,1000,700) > 0);
        Assert.Equal(RoutingDirectionHelper.CorridorTransitionCost(350,10,1000,1000,350),
            RoutingDirectionHelper.CorridorTransitionCost(10,350,1000,1000,350));
    }

    [Fact]
    public void GeographicCorridorsCannotAddAnUnboundedDelay()
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs = 1000,
            DroppedOrderBasePenaltySeconds = 1000000 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(101,6.01,-75,Now,Now,true,0),
            new RoutingNode(202,6.01,-74.999,Now,Now,true,0),
            new RoutingNode(303,6.0082,-74.9942,Now,Now,true,0),
            new RoutingNode(404,6.0077,-74.9935,Now,Now,true,0)]);
        for (var from = 0; from < 5; from++)
        for (var to = 0; to < 5; to++)
            matrix.DurationSeconds[from,to] = from == to ? 0 :
                from > 0 && to > 0 && ((from <= 2) == (to <= 2)) ? 1000 : 10;

        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            [new RoutingVehicleSlot(0),new RoutingVehicleSlot(0)],Now));

        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(2,result.Routes.Count);
        Assert.All(result.Routes,route=>Assert.InRange(route.ApproximateDurationSeconds,0,220));
    }

    [Fact]
    public void RealCoordinateCorridorsSeparateToscanaFromTheSouthernOrders()
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs = 1500,
            DroppedOrderBasePenaltySeconds = 100000 });
        (int Id,double Lat,double Lon)[] destinations = [
            (30,6.297360,-75.567386),(20,6.293091,-75.570820),(18,6.288625,-75.573932),
            (31,6.284402,-75.578441),(36,6.310454,-75.570210),(35,6.303879,-75.566611),
            (26,6.309520,-75.559221),(33,6.304766,-75.577928),(32,6.294037,-75.583867),
            (22,6.280336,-75.585504),(29,6.284744,-75.595465),(19,6.291033,-75.578738),
            (28,6.290600,-75.579075),(21,6.289582,-75.577574)];
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6.308047,-75.574383,
            destinations.Select(point=>new RoutingNode(point.Id,point.Lat,point.Lon,Now,Now,true,240)).ToArray());
        var groups = RoutingDirectionHelper.GroupCorridors(matrix,
            Enumerable.Range(0,destinations.Length).ToHashSet(),4);
        Assert.Equal(groups[5],groups[6]);
        Assert.Equal(groups[6],groups[7]);
        Assert.Equal(groups[12],groups[13]);
        Assert.Equal(groups[13],groups[14]);

        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            Enumerable.Range(0,4).Select(_=>new RoutingVehicleSlot(0)).ToArray(),Now));
        Assert.Empty(result.UnroutedNodeIndexes);
        var eastern = Assert.Single(result.Routes,route=>route.NodeIndexes.Contains(6));
        Assert.Equal(new[] {26,35,36},eastern.NodeIndexes.Select(index=>destinations[index].Id).Order());
        Assert.All(result.Routes,route=>Assert.Single(route.NodeIndexes.Select(index=>groups[index+1]).Distinct()));
    }

    [Fact]
    public void CoincidentDestinationsDoNotCreateEmptyCorridors()
    {
        var options = Options.Create(new DeliveryRoutingOptions());
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
            Enumerable.Range(0,4).Select(index=>new RoutingNode(index+1,6.01,-75,Now,Now,true,60)).ToArray());
        var groups = RoutingDirectionHelper.GroupCorridors(matrix,Enumerable.Range(0,4).ToHashSet(),4);
        Assert.Equal(-1,groups[0]);
        Assert.Single(groups.Skip(1).Distinct());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactNeighborhoodIsNotExtendedByDistantCollinearOrders(bool reorderAndRenumber)
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs=1500 });
        (int Id,double Lat,double Lon)[] destinations = [
            (30,6.297360,-75.567386),(20,6.293091,-75.570820),(18,6.288625,-75.573932),
            (31,6.284402,-75.578441),(26,6.309520,-75.559221),(33,6.304766,-75.577928),
            (32,6.294037,-75.583867),(22,6.280336,-75.585504),(29,6.284744,-75.595465),
            (19,6.291033,-75.578738),(28,6.290600,-75.579075),(21,6.289582,-75.577574)];
        if (reorderAndRenumber) Array.Reverse(destinations);
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6.308047,-75.574383,
            destinations.Select((point,index)=>new RoutingNode(reorderAndRenumber ? 1000+index : point.Id,
                point.Lat,point.Lon,Now.AddMinutes(-12),Now,true,240)).ToArray());
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            Enumerable.Range(0,4).Select(_=>new RoutingVehicleSlot(0)).ToArray(),Now));
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(4,result.Routes.Count);
        var grouped=result.Routes.Select(route=>route.NodeIndexes.Select(index=>destinations[index].Id).Order().ToArray()).ToArray();
        Assert.Contains(grouped,group=>group.SequenceEqual(new[] {19,21,28}));
        Assert.Contains(grouped,group=>group.SequenceEqual(new[] {18,20,30,31}));
        Assert.Contains(grouped,group=>group.SequenceEqual(new[] {22,29,32,33}));
        Assert.Contains(grouped,group=>group.SequenceEqual(new[] {26}));
    }

    [Fact]
    public void OrderWithinACorridorUsesTravelTimeInsteadOfSortingBearings()
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs=500,
            DroppedOrderBasePenaltySeconds=100000 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(1,6.01,-75,Now,Now,true,0),
            new RoutingNode(2,6.01,-74.999,Now,Now,true,0),
            new RoutingNode(3,6.01,-74.998,Now,Now,true,0)]);
        for (var from=0;from<4;from++)
        for (var to=0;to<4;to++)
            matrix.DurationSeconds[from,to] = from==to ? 0 : 100;
        matrix.DurationSeconds[0,1]=1;
        matrix.DurationSeconds[1,3]=1;
        matrix.DurationSeconds[3,2]=1;
        matrix.DurationSeconds[2,0]=1;
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        var route=Assert.Single(result.Routes);
        Assert.Equal(new[] {0,2,1},route.NodeIndexes);
        Assert.Equal(4,route.ApproximateDurationSeconds);
    }

    [Fact]
    public void CorridorSweepDoesNotSplitNeighborsAcrossNorth()
    {
        var options = Options.Create(new DeliveryRoutingOptions());
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(1,6.01,-75.001,Now,Now,true,60),
            new RoutingNode(2,6.01,-74.999,Now,Now,true,60),
            new RoutingNode(3,5.99,-75.001,Now,Now,true,60),
            new RoutingNode(4,5.99,-74.999,Now,Now,true,60)]);
        var groups=RoutingDirectionHelper.GroupCorridors(matrix,Enumerable.Range(0,4).ToHashSet(),2);
        Assert.Equal(groups[1],groups[2]);
        Assert.Equal(groups[3],groups[4]);
        Assert.NotEqual(groups[1],groups[3]);
    }

    [Fact]
    public void RobledoCannotShareTheGratamiraFlorenciaDeparture()
    {
        var options = Options.Create(new DeliveryRoutingOptions { DroppedOrderBasePenaltySeconds=100000,
            SolverTimeLimitMs=500 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6.308047,-75.574383,
        [new RoutingNode(34,6.283697,-75.590655,Now,Now,true,240),
            new RoutingNode(35,6.303879,-75.566611,Now,Now,true,240),
            new RoutingNode(36,6.310454,-75.570210,Now,Now,true,240)]);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            [new RoutingVehicleSlot(0),new RoutingVehicleSlot(0)],Now));
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Contains(result.Routes,r=>r.NodeIndexes.Count==2 && r.NodeIndexes.Contains(1) && r.NodeIndexes.Contains(2));
        Assert.Contains(result.Routes,r=>r.NodeIndexes.Count==1 && r.NodeIndexes.Contains(0));
    }

    [Fact]
    public void AdjacentTurnsCannotChainIntoAnOppositeDirection()
    {
        var options = Options.Create(new DeliveryRoutingOptions { DroppedOrderBasePenaltySeconds=100000,
            SolverTimeLimitMs=250 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(1,6.01,-75,Now,Now,true,60),
            new RoutingNode(2,6.005,-74.9913,Now,Now,true,60),
            new RoutingNode(3,5.995,-74.9913,Now,Now,true,60)]);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        Assert.Equal(2,Assert.Single(result.Routes).NodeIndexes.Count);
        Assert.Single(result.UnroutedNodeIndexes);
    }

    [Fact]
    public void ApproximateMatrix_IsSymmetricAndUsesRoadFactor()
    {
        var provider = new ApproximateRoutingCostMatrixProvider(Options.Create(new DeliveryRoutingOptions
        {
            ApproximateRoadFactor = 1.3,
            ApproximateUrbanSpeedKph = 20,
        }));
        var matrix = provider.Create(7.1254, -73.1198,
        [
            new RoutingNode(10, 7.1304, -73.1198, Now, Now, true, 240),
            new RoutingNode(11, 7.1254, -73.1148, Now, Now, true, 240),
        ]);

        Assert.Equal(3, matrix.DurationSeconds.GetLength(0));
        Assert.True(matrix.DistanceMeters[0, 1] > 500);
        Assert.Equal(matrix.DistanceMeters[1, 2], matrix.DistanceMeters[2, 1]);
        Assert.Equal(matrix.DurationSeconds[0, 2], matrix.DurationSeconds[2, 0]);
        Assert.True(matrix.BearingFromBranchDegrees[1] <= 10 || matrix.BearingFromBranchDegrees[1] >= 350);
        Assert.InRange(matrix.BearingFromBranchDegrees[2], 80, 100);
    }

    [Fact]
    public void Optimizer_RoutesAllEligibleNodesWithoutStopLimit()
    {
        var options = Options.Create(new DeliveryRoutingOptions
        {
            SolverTimeLimitMs = 500,
            DirectionPenaltyPerDegreeSeconds = 0,
            DroppedOrderBasePenaltySeconds = 100_000,
        });
        var provider = new ApproximateRoutingCostMatrixProvider(options);
        var nodes = Enumerable.Range(0, 12)
            .Select(index => new RoutingNode(
                index + 1,
                7.1254 + index * 0.0002,
                -73.1198 + index * 0.0001,
                Now.AddMinutes(-index),
                Now,
                true,
                60))
            .ToArray();
        var matrix = provider.Create(7.1254, -73.1198, nodes);

        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(
            new DeliveryRouteOptimizationRequest(matrix, [new RoutingVehicleSlot(0)], Now));

        var route = Assert.Single(result.Routes);
        Assert.Equal(12, route.NodeIndexes.Count);
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(Enumerable.Range(0, 12).Order(), route.NodeIndexes.Order());
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    public void ReadyLocalOrdersTakePriorityOverUncertainPreparationAndDistantDetours(int ageMinutes)
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs = 1500 });
        (double Lat, double Lon)[] ready = [
            (6.288625,-75.573932),(6.291033,-75.578738),(6.293091,-75.570820),
            (6.289582,-75.577574),(6.280336,-75.585504),(6.309520,-75.559221),
            (6.377581,-75.446200),(6.290600,-75.579075),(6.284744,-75.595465),
            (6.297360,-75.567386),(6.284402,-75.578441),(6.294037,-75.583867),(6.304766,-75.577928)];
        var nodes = ready.Select((p,i) => new RoutingNode(i+1,p.Lat,p.Lon,
            Now.AddMinutes(-ageMinutes),Now,true,240) { WaitDeadlineAtUtc = Now.AddMinutes(1) }).ToList();
        (double Lat, double Lon)[] pending = [(6.289412,-75.572831),(6.291668,-75.575081),
            (6.276844,-75.579704),(6.283697,-75.590655),(6.303879,-75.566611),(6.310454,-75.570210)];
        nodes.AddRange(pending.Select((p,i) => new RoutingNode(14+i,p.Lat,p.Lon,
            Now.AddMinutes(i<3 ? -26 : -9),Now.AddMinutes(i<3 ? 9 : 4),false,240)
            { ReadyWithinWindowProbability = .5 }));
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6.308047,-75.574383,nodes);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            Enumerable.Range(0,4).Select(_=>new RoutingVehicleSlot(0)).ToArray(),Now));
        var served = result.Routes.SelectMany(r=>r.NodeIndexes).ToHashSet();
        Assert.All(Enumerable.Range(0,13),i=>Assert.Contains(i,served));
        Assert.NotNull(matrix.Nodes[6].DeliveryTimeLimitSeconds);
        Assert.All(result.Routes.Where(route => route.NodeIndexes.Any(index => index < 13)),
            route => Assert.All(route.NodeIndexes, index => Assert.True(matrix.Nodes[index].IsReady)));
        Assert.InRange(result.Routes.Count,1,4);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ExpiredWaitingDeadlineRequiresLeavingOrSplitting(int vehicles, bool allRouted)
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs = 150,
            DroppedOrderBasePenaltySeconds = 100000, DirectionPenaltyPerDegreeSeconds = 0 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6, -75,
        [new RoutingNode(1, 6.001, -75, Now.AddMinutes(-20), Now, true, 30) { WaitDeadlineAtUtc = Now },
            new RoutingNode(2, 6.002, -75, Now.AddMinutes(-10), Now.AddSeconds(120), false, 30)]);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            Enumerable.Range(0, vehicles).Select(_ => new RoutingVehicleSlot(0)).ToArray(), Now));
        Assert.All(result.Routes, route => Assert.False(route.NodeIndexes.Contains(0) && route.NodeIndexes.Contains(1)));
        Assert.Equal(allRouted, result.UnroutedNodeIndexes.Count == 0);
        Assert.Equal(result.Routes.SelectMany(r => r.NodeIndexes).Distinct().Count(), result.Routes.Sum(r => r.NodeIndexes.Count));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    public void OldReadyOrdersKeepTheirTravelBudget(int ageMinutes)
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs=300 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
            Enumerable.Range(0,6).Select(i=>new RoutingNode(i+1,6.001+i*.0001,-75,
                Now.AddMinutes(-ageMinutes),Now,true,240)).ToArray());
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(6,Assert.Single(result.Routes).NodeIndexes.Count);
    }

    [Fact]
    public void RecoveryIncludesAnAffordablePendingStopWithoutRemovingSelectedOrders()
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs=300,
            DroppedOrderBasePenaltySeconds=100,MaximumAdditionalDelaySeconds=0 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(1,6.001,-75,Now,Now,true,0),new RoutingNode(2,6.002,-75,Now,Now,true,0)]);
        matrix.DurationSeconds[0,1]=matrix.DurationSeconds[1,0]=10;
        matrix.DurationSeconds[0,2]=matrix.DurationSeconds[2,0]=300;
        matrix.DurationSeconds[1,2]=matrix.DurationSeconds[2,1]=300;
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(new[] {0,1},Assert.Single(result.Routes).NodeIndexes);
    }

    [Fact]
    public void DeliveryLimitStillAppliesWhenExcludingOrdersIsExpensive()
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs=300,
            MaximumDeliveryAfterDepartureSeconds=300,MaximumCompletionDeliverySeconds=300,DroppedOrderBasePenaltySeconds=100000 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
            Enumerable.Range(0,5).Select(i=>new RoutingNode(i+1,6.001,-75,Now,Now,true,240)).ToArray());
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        Assert.Equal(2,Assert.Single(result.Routes).NodeIndexes.Count);
        Assert.Equal(3,result.UnroutedNodeIndexes.Count);
    }

    [Fact]
    public void ChronologicalSelectionUsesDatesNotIdsOrInputOrder()
    {
        var options = Options.Create(new DeliveryRoutingOptions());
        var nodes = Enumerable.Range(0,13).Select(index => new RoutingNode(1000-index,6.01,-75,
            Now.AddMinutes(-50+index*2),Now,true,240)).Reverse().ToArray();
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,nodes);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        var ids = result.Routes.SelectMany(route => route.NodeIndexes).Select(index => nodes[index].OrderId).Order().ToArray();
        Assert.Equal(Enumerable.Range(991,10),ids);
        Assert.Equal(new[] {988,989,990},result.UnroutedNodeIndexes.Select(index => nodes[index].OrderId).Order());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DistantExceptionsCannotDisplaceOlderOrders(int couriers)
    {
        var options = Options.Create(new DeliveryRoutingOptions());
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(90,6.01,-75,Now.AddMinutes(-50),Now,true,240),
            new RoutingNode(5,6,-74.99,Now.AddMinutes(-40),Now,true,240),
            new RoutingNode(1,5.8,-75,Now.AddMinutes(-10),Now,true,240)]);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,
            Enumerable.Range(0,couriers).Select(_ => new RoutingVehicleSlot(0)).ToArray(),Now));
        Assert.Contains(0,result.Routes.SelectMany(route => route.NodeIndexes));
        if(couriers==2) Assert.Contains(1,result.Routes.SelectMany(route => route.NodeIndexes));
        Assert.Contains(2,result.UnroutedNodeIndexes);
    }

    [Theory]
    [InlineData(1,3)]
    [InlineData(1,5)]
    [InlineData(2,3)]
    [InlineData(2,5)]
    [InlineData(3,3)]
    [InlineData(3,5)]
    public void OriginalAddressPackagesRespectAgeDirectionsAndIndividualLimits(int package, int couriers)
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"delivery-routing-addresses.json")));
        var points = fixture.RootElement[package-1].EnumerateArray().ToArray();
        var nodes = points.Select((point,index) => new RoutingNode(18+index,point[3].GetDouble(),point[4].GetDouble(),
            Now.AddMinutes(-50+index*2),Now,true,240)).ToArray();
        var options = Options.Create(new DeliveryRoutingOptions());
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6.308047,-75.574383,nodes);
        var request = new DeliveryRouteOptimizationRequest(matrix,Enumerable.Range(0,couriers).Select(_ => new RoutingVehicleSlot(0)).ToArray(),Now);
        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(request);
        var included = result.Routes.SelectMany(route => route.NodeIndexes).ToArray();
        Assert.Equal(included.Length,included.Distinct().Count());
        Assert.Equal(20,included.Length+result.UnroutedNodeIndexes.Count);
        Assert.Contains(0,included);
        Assert.InRange(result.Routes.Count,1,couriers);
        foreach(var route in result.Routes)
        {
            var schedule = RoutingScheduleHelper.Calculate(request,route.VehicleIndex,route.NodeIndexes);
            Assert.True(RoutingScheduleHelper.Fits(request,route.VehicleIndex,route.NodeIndexes,schedule,new Dictionary<int,long>(),options.Value));
        }
        if(package==1 && couriers==3) Assert.All(Enumerable.Range(0,10),index => Assert.Contains(index,included));
        if(couriers==5) Assert.Equal(20,included.Length);
    }

    [Fact]
    public void ALocalCentralStopDoesNotJoinOppositeExternalDirections()
    {
        var options=Options.Create(new DeliveryRoutingOptions());
        var matrix=new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
        [new RoutingNode(1,6.0001,-75,Now,Now,true,240),
            new RoutingNode(2,6.01,-75,Now,Now,true,240),new RoutingNode(3,5.99,-75,Now,Now,true,240)]);
        Assert.True(RoutingScheduleHelper.CanShare(matrix,0,1,options.Value));
        Assert.True(RoutingScheduleHelper.CanShare(matrix,0,2,options.Value));
        Assert.False(RoutingScheduleHelper.CanShare(matrix,1,2,options.Value));
        var result=new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        Assert.Equal(2,Assert.Single(result.Routes).NodeIndexes.Count);
    }

    [Fact]
    public void ArrivalAllowanceIsCumulativeAndCannotDelayAnOnTimeReservation()
    {
        var options=new DeliveryRoutingOptions();
        var matrix=new ApproximateRoutingCostMatrixProvider(Options.Create(options)).Create(6,-75,
        [new RoutingNode(1,6.01,-75,Now,Now,true,240),
            new RoutingNode(2,6.01,-75,Now,Now,true,240) {DeliveryDueAtUtc=Now.AddSeconds(800)}]);
        var request=new DeliveryRouteOptimizationRequest(matrix,[new RoutingVehicleSlot(0)],Now);
        Assert.Equal(900,RoutingScheduleHelper.LatestArrival(request,0,600,options));
        Assert.Equal(800,RoutingScheduleHelper.LatestArrival(request,1,600,options));
        Assert.Equal(1000,RoutingScheduleHelper.LatestArrival(request,1,1000,options));
        var limits=new Dictionary<int,long>{{0,900}};
        matrix.DurationSeconds[0,1]=901;
        Assert.False(RoutingScheduleHelper.Fits(request,0,[0],RoutingScheduleHelper.Calculate(request,0,[0]),limits,options));
    }

    [Fact]
    public void RebalancingSplitsAFullerRouteToAvoidAnUnnecessarySoloDeparture()
    {
        var options=Options.Create(new DeliveryRoutingOptions());
        var matrix=new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
            Enumerable.Range(0,5).Select(index=>new RoutingNode(index+1,index==0 ? 6.08 : 6.001,-75,Now,Now,true,240)).ToArray());
        Assert.Null(matrix.Nodes[0].DeliveryTimeLimitSeconds);
        for(var from=0;from<6;from++)
        for(var to=0;to<6;to++)
            matrix.DurationSeconds[from,to]=from==to ? 0 : from==1 || to==1 ? (from==0 || to==0 ? 2000 : 1980) : 10;
        var request=new DeliveryRouteOptimizationRequest(matrix,[new RoutingVehicleSlot(0),new RoutingVehicleSlot(0)],Now);
        var result=new OrToolsDeliveryRouteOptimizer(options).Optimize(request);
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(2,result.Routes.Count);
        Assert.All(result.Routes,route=>Assert.True(route.NodeIndexes.Count>=2));
        var distant=Assert.Single(result.Routes,route=>route.NodeIndexes.Contains(0));
        var schedule=RoutingScheduleHelper.Calculate(request,distant.VehicleIndex,distant.NodeIndexes);
        Assert.InRange(schedule.Arrivals[distant.NodeIndexes.ToList().IndexOf(0)],2101,2300);
    }

    [Fact]
    public void MinimumSearchBudgetStillPreservesAFeasibleInitialProposal()
    {
        var options=Options.Create(new DeliveryRoutingOptions {SolverTimeLimitMs=1});
        var matrix=new ApproximateRoutingCostMatrixProvider(options).Create(6,-75,
            Enumerable.Range(0,6).Select(index=>new RoutingNode(index+1,6.001,-75,Now,Now,true,240)).ToArray());
        var result=new OrToolsDeliveryRouteOptimizer(options).Optimize(new(matrix,[new RoutingVehicleSlot(0)],Now));
        Assert.NotEmpty(result.Routes);
        Assert.Empty(result.UnroutedNodeIndexes);
    }
}
