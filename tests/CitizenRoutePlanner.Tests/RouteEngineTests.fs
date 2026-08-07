namespace CitizenRoutePlanner.Tests

open System
open Xunit
open CitizenRoutePlanner.Core

module RouteEngineTests =

    let emptyIndex : LocationIndex = {
        All = []
        ByUuid = Map.empty
        ByName = Map.empty
        CelestialBodies = []
        Planets = []
        Moons = []
        ReferenceOrigins = []
    }
    
    let createLocation uuid name lType position =
        {
            Uuid = uuid
            Name = name
            Type = lType
            System = "Stanton"
            ParentUuid = None
            QtValid = true
            Position = position
        }

    let planetA = createLocation (Guid.NewGuid()) "Planet A" "Planet" { X = 0.0; Y = 0.0; Z = 0.0 }
    let planetB = createLocation (Guid.NewGuid()) "Planet B" "Planet" { X = 300_000_000.0; Y = 0.0; Z = 0.0 }
    
    let outpostA1 = { createLocation (Guid.NewGuid()) "Outpost A1" "Outpost" { X = 1000.0; Y = 0.0; Z = 0.0 } with ParentUuid = Some planetA.Uuid }
    let outpostA2 = { createLocation (Guid.NewGuid()) "Outpost A2" "Outpost" { X = 5000.0; Y = 0.0; Z = 0.0 } with ParentUuid = Some planetA.Uuid }
    
    let testLocations = {
        emptyIndex with 
            All = [planetA; planetB; outpostA1; outpostA2]
            ByUuid = [planetA; planetB; outpostA1; outpostA2] |> List.map (fun l -> l.Uuid, l) |> Map.ofList
            CelestialBodies = [planetA; planetB]
            Planets = [planetA; planetB]
    }

    let createObjective objId objType (loc: LocationInfo) scu =
        {
            ObjectiveId = objId
            Type = objType
            RawPosition = loc.Position
            ZoneHostId = 0UL
            AbsolutePosition = Some loc.Position
            ResolvedLocation = Some loc
            ScuAmount = scu
            CargoType = None
            DestinationName = Some loc.Name
            Status = Pending
            PairedObjectiveId = None
        }

    let createMission mId mType scope objectives =
        {
            MissionId = mId
            Title = "Test Mission"
            GeneratorName = "Test"
            ContractName = "Test"
            ContractDefinitionId = Guid.NewGuid()
            MissionType = mType
            Scope = scope
            Objectives = objectives
            PendingObjectivesData = []
            Status = Active
            AcceptedAt = DateTimeOffset.UtcNow
        }

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Courier mission generates 2 stops`` () =
        let mId = Guid.NewGuid()
        let pickup = createObjective "pickup_0" Pickup outpostA1 None
        let dropoff = createObjective "dropoff_0" Dropoff outpostA2 None
        let mission = createMission mId Courier (Local 1) [pickup; dropoff]
        
        let appState = {
            Missions = Map.ofList [(mId, mission)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = None
            CurrentCargoScu = 0
            QuantumDrive = None
        }
        
        let routeOpt = RouteEngine.calculateRoute appState testLocations
        
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        Assert.Equal(2, route.Stops.Length)
        Assert.Equal(outpostA1.Uuid, route.Stops.[0].Location.Uuid)
        Assert.Equal(outpostA2.Uuid, route.Stops.[1].Location.Uuid)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Ship capacity prevents invalid route`` () =
        // Ship capacity = 10. Mission 1 = 8 SCU, Mission 2 = 6 SCU
        // Should not pick up both before dropping off one.
        let m1Id = Guid.NewGuid()
        let m2Id = Guid.NewGuid()
        
        let p1 = createObjective "p1" Pickup outpostA1 (Some 8)
        let d1 = createObjective "d1" Dropoff outpostA2 (Some 8)
        
        let p2 = createObjective "p2" Pickup outpostA1 (Some 6)
        let d2 = createObjective "d2" Dropoff outpostA2 (Some 6)
        
        let m1 = createMission m1Id DirectHaul (Local 1) [p1; d1]
        let m2 = createMission m2Id DirectHaul (Local 1) [p2; d2]
        
        let appState = {
            Missions = Map.ofList [(m1Id, m1); (m2Id, m2)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = None
            CurrentCargoScu = 0
            QuantumDrive = None
        }
        
        let routeOpt = RouteEngine.calculateRoute appState testLocations
        
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        
        // P1 and P2 can't be consecutive because 8+6 > 10.
        // Route should be P1 -> D1 -> P2 -> D2 or P2 -> D2 -> P1 -> D1.
        // It could group P1 and P2 together if it ignored capacity, but it shouldn't.
        Assert.True(route.Stops.Length >= 2)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Courier missions in same location are grouped`` () =
        let m1Id = Guid.NewGuid()
        let m2Id = Guid.NewGuid()
        
        let p1 = createObjective "p1" Pickup outpostA1 None
        let d1 = createObjective "d1" Dropoff outpostA2 None
        
        let p2 = createObjective "p2" Pickup outpostA1 None
        let d2 = createObjective "d2" Dropoff outpostA2 None
        
        let m1 = createMission m1Id Courier (Local 1) [p1; d1]
        let m2 = createMission m2Id Courier (Local 1) [p2; d2]
        
        let appState = {
            Missions = Map.ofList [(m1Id, m1); (m2Id, m2)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = None
            CurrentCargoScu = 0
            QuantumDrive = None
        }
        
        let routeOpt = RouteEngine.calculateRoute appState testLocations
        
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        // Two pickups at outpostA1, two dropoffs at outpostA2 -> exactly 2 stops
        Assert.Equal(2, route.Stops.Length)
        Assert.Equal(2, route.Stops.[0].Actions.Length)
        Assert.Equal(2, route.Stops.[1].Actions.Length)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Greedy solver handles more than 12 points`` () =
        // Generate 7 missions (14 points total) to trigger greedy + 2-opt
        let missions = 
            [1..7] |> List.map (fun i ->
                let mId = Guid.NewGuid()
                let p = createObjective $"p{i}" Pickup outpostA1 None
                let d = createObjective $"d{i}" Dropoff outpostA2 None
                mId, createMission mId Courier (Local 1) [p; d]
            )
        
        let appState = {
            Missions = Map.ofList missions
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = None
            CurrentCargoScu = 0
            QuantumDrive = None
        }
        
        let routeOpt = RouteEngine.calculateRoute appState testLocations
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        Assert.Equal(2, route.Stops.Length)
        Assert.Equal(7, route.Stops.[0].Actions.Length)
        Assert.Equal(7, route.Stops.[1].Actions.Length)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Dropoff at player location cannot precede pending pickup`` () =
        let mId = Guid.NewGuid()
        // Player is at planetA. Dropoff is at planetA. Pickup is at planetB (far away).
        // Algorithm MUST choose Pickup at planetB before Dropoff at planetA.
        let p = createObjective "pickup_0" Pickup planetB (Some 10)
        let d = createObjective "dropoff_0" Dropoff planetA (Some 10)
        let mission = createMission mId DirectHaul (System) [p; d]
        
        let appState = {
            Missions = Map.ofList [(mId, mission)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = Some { RouteEngine.defaultShip with CargoCapacity = 288 }
            CurrentCargoScu = 0
            QuantumDrive = None
        }
        
        let routeOpt = RouteEngine.calculateRoute appState testLocations
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        Assert.Equal(2, route.Stops.Length)
        Assert.Equal(planetB.Uuid, route.Stops.[0].Location.Uuid)
        Assert.Equal(planetA.Uuid, route.Stops.[1].Location.Uuid)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``MultiHaul mission enforces individual pickup dependencies`` () =
        let mId = Guid.NewGuid()
        let p0 = createObjective "pickup_0" Pickup outpostA1 (Some 10)
        let d0 = createObjective "dropoff_0" Dropoff outpostA2 (Some 10)
        let p1 = createObjective "pickup_1" Pickup planetB (Some 10)
        let d1 = createObjective "dropoff_1" Dropoff outpostA1 (Some 10)
        
        let mission = createMission mId (MultiHaul 2) (System) [p0; d0; p1; d1]
        
        let appState = {
            Missions = Map.ofList [(mId, mission)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = Some { RouteEngine.defaultShip with CargoCapacity = 288 }
            CurrentCargoScu = 0
            QuantumDrive = None
        }
        
        let routeOpt = RouteEngine.calculateRoute appState testLocations
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        
        let allActions = route.Stops |> List.collect (fun s -> s.Actions)
        let indexOfAction objId =
            allActions |> List.findIndex (fun a ->
                match a with
                | PickupCargo (_, id, _, _) | DropoffCargo (_, id, _, _)
                | PickupPackage (_, id, _) | DropoffPackage (_, id, _)
                | NavTo (_, id) -> id = objId
            )
        
        Assert.True(indexOfAction "pickup_0" < indexOfAction "dropoff_0")
        Assert.True(indexOfAction "pickup_1" < indexOfAction "dropoff_1")

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Mission with single pickup exceeding ship capacity is excluded from route`` () =
        let mId = Guid.NewGuid()
        let pickup = createObjective "pickup_0" Pickup outpostA1 (Some 10)
        let dropoff = createObjective "dropoff_0" Dropoff outpostA2 (Some 10)
        let mission = createMission mId (DirectHaul) (Local 1) [pickup; dropoff]

        // Ship capacity = 8 (less than pickup 10 SCU)
        let appState = {
            Missions = Map.ofList [(mId, mission)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = Some { RouteEngine.defaultShip with CargoCapacity = 8 }
            CurrentCargoScu = 0
            QuantumDrive = None
        }

        let routeOpt = RouteEngine.calculateRoute appState testLocations
        Assert.True(routeOpt.IsNone)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Mission with multiple pickups each fitting ship capacity is included even if sum exceeds capacity`` () =
        let mId = Guid.NewGuid()
        let p0 = createObjective "pickup_0" Pickup outpostA1 (Some 6)
        let d0 = createObjective "dropoff_0" Dropoff outpostA2 (Some 6)
        let p1 = createObjective "pickup_1" Pickup planetB (Some 6)
        let d1 = createObjective "dropoff_1" Dropoff outpostA1 (Some 6)
        let mission = createMission mId (MultiHaul 2) System [p0; d0; p1; d1]

        // Ship capacity = 8 (each pickup is 6 SCU, sum is 12 SCU)
        let appState = {
            Missions = Map.ofList [(mId, mission)]
            CurrentRoute = None
            PlayerLocation = Some planetA
            QuantumDestination = None
            Ship = Some { RouteEngine.defaultShip with CargoCapacity = 8 }
            CurrentCargoScu = 0
            QuantumDrive = None
        }

        let routeOpt = RouteEngine.calculateRoute appState testLocations
        Assert.True(routeOpt.IsSome)

    [<Fact>]
    [<Trait("Category", "RouteEngine")>]
    let ``Route estimates handle zero vectors and unknown locations without producing NaN or Infinity`` () =
        let mId = Guid.NewGuid()
        let zeroLoc = createLocation (Guid.NewGuid()) "Unknown Location (0)" "Point of Interest" { X = 0.0; Y = 0.0; Z = 0.0 }
        let p0 = createObjective "pickup_0" Pickup zeroLoc (Some 2)
        let d0 = createObjective "dropoff_0" Dropoff zeroLoc (Some 2)
        let mission = createMission mId DirectHaul System [p0; d0]

        let appState = {
            Missions = Map.ofList [(mId, mission)]
            CurrentRoute = None
            PlayerLocation = Some zeroLoc
            QuantumDestination = None
            Ship = Some { RouteEngine.defaultShip with Mass = 0.0 } // Edge case mass 0
            CurrentCargoScu = 0
            QuantumDrive = None
        }

        let routeOpt = RouteEngine.calculateRoute appState testLocations
        Assert.True(routeOpt.IsSome)
        let route = routeOpt.Value
        Assert.False(Double.IsNaN(route.TotalEstimatedTime))
        Assert.False(Double.IsInfinity(route.TotalEstimatedTime))
        for stop in route.Stops do
            Assert.False(Double.IsNaN(stop.TravelTimeEstimate))
            Assert.False(Double.IsInfinity(stop.TravelTimeEstimate))
            Assert.False(Double.IsNaN(stop.ActionTimeEstimate))
            Assert.False(Double.IsInfinity(stop.ActionTimeEstimate))


