namespace CitizenRoutePlanner.Tests

open System
open Xunit
open CitizenRoutePlanner.Core

module MissionManagerTests =

    let createEmptyIndex () =
        {
            All = []
            ByUuid = Map.empty
            ByName = Map.empty
            CelestialBodies = []
            Planets = []
            Moons = []
        }

    let mkTime (sec: int) = DateTimeOffset.UtcNow.AddSeconds(float sec)

    [<Fact>]
    let ``Full Lifecycle Courier Mission`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let ts0 = mkTime 0

        let ev1 = LogParser.ContractAccepted (ts0, missionId, "Courier Test")
        let st1 = MissionManager.processEvent index MissionManager.initialState ev1
        
        Assert.True(st1.Missions.ContainsKey(missionId))
        let m1 = st1.Missions.[missionId]
        Assert.Equal("Courier Test", m1.Title)
        Assert.Equal(0, m1.Objectives.Length)

        let ev2 = LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Generator", "FTL_Courier_Stanton", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=10.;Y=20.;Z=30.})
        let ev3 = LogParser.ObjectiveMarkerCreated (mkTime 2, missionId, "Generator", "FTL_Courier_Stanton", Guid.NewGuid(), "dropoff_0", Dropoff, 2UL, {X=100.;Y=200.;Z=300.})

        let st2 = MissionManager.processEvent index st1 ev2
        let st3 = MissionManager.processEvent index st2 ev3

        let m3 = st3.Missions.[missionId]
        Assert.Equal(2, m3.Objectives.Length)
        Assert.Equal(Courier, m3.MissionType)

        let ev4 = LogParser.ObjectiveStateChanged (mkTime 3, missionId, "pickup_0", ObjectiveStatus.Completed)
        let st4 = MissionManager.processEvent index st3 ev4

        let m4 = st4.Missions.[missionId]
        let pObj = m4.Objectives |> List.find (fun o -> o.ObjectiveId = "pickup_0")
        Assert.Equal(ObjectiveStatus.Completed, pObj.Status)

        let ev5 = LogParser.ObjectiveStateChanged (mkTime 4, missionId, "dropoff_0", ObjectiveStatus.Completed)
        let st5 = MissionManager.processEvent index st4 ev5

        let ev6 = LogParser.ContractCompleted (mkTime 5, missionId, "Courier Test")
        let st6 = MissionManager.processEvent index st5 ev6

        Assert.Equal(MissionStatus.Completed, st6.Missions.[missionId].Status)

    [<Fact>]
    let ``Covalex Multihauler`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let ev1 = LogParser.ContractAccepted (mkTime 0, missionId, "Multi Test")
        let st1 = MissionManager.processEvent index MissionManager.initialState ev1
        
        let st2 = MissionManager.processEvent index st1 (LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Gen", "HaulCargo_SingleToMulti4_RefinedOre", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=0.;Y=0.;Z=0.}))
        let st3 = MissionManager.processEvent index st2 (LogParser.ObjectiveMarkerCreated (mkTime 2, missionId, "Gen", "HaulCargo_SingleToMulti4_RefinedOre", Guid.NewGuid(), "dropoff_0", Dropoff, 2UL, {X=1.;Y=1.;Z=1.}))
        let st4 = MissionManager.processEvent index st3 (LogParser.ObjectiveMarkerCreated (mkTime 3, missionId, "Gen", "HaulCargo_SingleToMulti4_RefinedOre", Guid.NewGuid(), "dropoff_1", Dropoff, 3UL, {X=2.;Y=2.;Z=2.}))
        let st5 = MissionManager.processEvent index st4 (LogParser.ObjectiveMarkerCreated (mkTime 4, missionId, "Gen", "HaulCargo_SingleToMulti4_RefinedOre", Guid.NewGuid(), "dropoff_2", Dropoff, 4UL, {X=3.;Y=3.;Z=3.}))
        let st6 = MissionManager.processEvent index st5 (LogParser.ObjectiveMarkerCreated (mkTime 5, missionId, "Gen", "HaulCargo_SingleToMulti4_RefinedOre", Guid.NewGuid(), "dropoff_3", Dropoff, 5UL, {X=4.;Y=4.;Z=4.}))

        let m6 = st6.Missions.[missionId]
        Assert.Equal(MultiHaul 4, m6.MissionType)
        Assert.Equal(5, m6.Objectives.Length)

    [<Fact>]
    let ``Player Position updates on Completion`` () =
        let loc = { Uuid = Guid.NewGuid(); Name = "TestLoc"; Type = "Outpost"; System = "Stanton"; ParentUuid = None; QtValid = true; Position = {X=0.;Y=0.;Z=0.} }
        let index = { createEmptyIndex() with ByName = Map.ofList [("testloc", loc)] }
        
        let missionId = Guid.NewGuid()
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.ContractAccepted (mkTime 0, missionId, "Pos Test"))
        let st2 = MissionManager.processEvent index st1 (LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Gen", "Contract", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=0.;Y=0.;Z=0.}))
        let st3 = MissionManager.processEvent index st2 (LogParser.NewObjective (mkTime 2, missionId, Some "pickup_0", Some Pickup, None, None, None, Some "TestLoc"))
        
        let m3 = st3.Missions.[missionId]
        let pObj = m3.Objectives |> List.find (fun o -> o.ObjectiveId = "pickup_0")
        Assert.True(pObj.ResolvedLocation.IsSome)

        let st4 = MissionManager.processEvent index st3 (LogParser.ObjectiveStateChanged (mkTime 3, missionId, "pickup_0", ObjectiveStatus.Completed))
        Assert.True(st4.PlayerLocation.IsSome)
        Assert.Equal("TestLoc", st4.PlayerLocation.Value.Name)

    [<Fact>]
    let ``Player Position updates on QuantumArrived`` () =
        let startLoc = { Uuid = Guid.NewGuid(); Name = "MicroTech"; Type = "Planet"; System = "Stanton"; ParentUuid = None; QtValid = true; Position = {X=0.;Y=0.;Z=0.} }
        let destLoc = { Uuid = Guid.NewGuid(); Name = "Crusader"; Type = "Planet"; System = "Stanton"; ParentUuid = None; QtValid = true; Position = {X=100.;Y=100.;Z=100.} }
        let index = { createEmptyIndex() with ByName = Map.ofList [("microtech", startLoc); ("crusader", destLoc)] }
        
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.QuantumRouteCalculated (mkTime 1, "MicroTech", "Crusader"))
        
        Assert.True(st1.PlayerLocation.IsSome)
        Assert.Equal("MicroTech", st1.PlayerLocation.Value.Name)
        Assert.True(st1.QuantumDestination.IsSome)
        Assert.Equal("Crusader", st1.QuantumDestination.Value.Name)

        let st2 = MissionManager.processEvent index st1 (LogParser.QuantumArrived (mkTime 2))
        
        Assert.True(st2.PlayerLocation.IsSome)
        Assert.Equal("Crusader", st2.PlayerLocation.Value.Name)
        Assert.True(st2.QuantumDestination.IsNone)

    [<Fact>]
    let ``NewObjective before CreateMarker handled correctly`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.ContractAccepted (mkTime 0, missionId, "Cargo Test"))
        
        // NewObjective arrives BEFORE CreateMarker for HaulCargo (no objectiveId)
        let st2 = MissionManager.processEvent index st1 (LogParser.NewObjective (mkTime 1, missionId, None, Some Dropoff, Some 0, Some 10, Some "Medical Supplies", Some "Dest"))
        
        // It should be stored in pending data
        let m2 = st2.Missions.[missionId]
        Assert.Equal(1, m2.PendingObjectivesData.Length)

        // Then CreateMarker arrives
        let st3 = MissionManager.processEvent index st2 (LogParser.ObjectiveMarkerCreated (mkTime 2, missionId, "Gen", "HaulCargo", Guid.NewGuid(), "dropoff_0", Dropoff, 1UL, {X=0.;Y=0.;Z=0.}))
        
        let m3 = st3.Missions.[missionId]
        Assert.Equal(0, m3.PendingObjectivesData.Length)
        let obj = m3.Objectives.Head
        Assert.Equal(Some 10, obj.ScuAmount)
        Assert.Equal(Some "Medical Supplies", obj.CargoType)
        Assert.Equal(Some "Dest", obj.DestinationName)

    [<Fact>]
    let ``Idempotent CreateMarker`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let ev1 = LogParser.ContractAccepted (mkTime 0, missionId, "Courier Test")
        let st1 = MissionManager.processEvent index MissionManager.initialState ev1
        let ev2 = LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Generator", "FTL_Courier_Stanton", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=10.;Y=20.;Z=30.})
        
        let st2 = MissionManager.processEvent index st1 ev2
        let st3 = MissionManager.processEvent index st2 ev2

        let m = st3.Missions.[missionId]
        Assert.Equal(1, m.Objectives.Length)

    [<Fact>]
    let ``ContractAccepted after ObjectiveMarkerCreated preserves existing objectives`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let evMarker = LogParser.ObjectiveMarkerCreated (mkTime 0, missionId, "Generator", "FTL_Courier_Stanton", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=10.;Y=20.;Z=30.})
        let st1 = MissionManager.processEvent index MissionManager.initialState evMarker
        
        Assert.Equal(1, st1.Missions.[missionId].Objectives.Length)

        let evAccepted = LogParser.ContractAccepted (mkTime 1, missionId, "Late Contract Title")
        let st2 = MissionManager.processEvent index st1 evAccepted

        Assert.Equal("Late Contract Title", st2.Missions.[missionId].Title)
        Assert.Equal(1, st2.Missions.[missionId].Objectives.Length)

    [<Fact>]
    let ``Cargo SCU updates on Objective completion`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.ContractAccepted (mkTime 0, missionId, "Haul Test"))
        let st2 = MissionManager.processEvent index st1 (LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Gen", "HaulCargo", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=0.;Y=0.;Z=0.}))
        let st3 = MissionManager.processEvent index st2 (LogParser.NewObjective (mkTime 2, missionId, Some "pickup_0", Some Pickup, Some 0, Some 16, Some "Medical", Some "Dest"))
        let st4 = MissionManager.processEvent index st3 (LogParser.ObjectiveMarkerCreated (mkTime 3, missionId, "Gen", "HaulCargo", Guid.NewGuid(), "dropoff_0", Dropoff, 2UL, {X=1.;Y=1.;Z=1.}))
        let st5 = MissionManager.processEvent index st4 (LogParser.NewObjective (mkTime 4, missionId, Some "dropoff_0", Some Dropoff, Some 0, Some 16, Some "Medical", Some "Dest"))

        Assert.Equal(0, st5.CurrentCargoScu)

        // Pickup complete -> SCU increases
        let st6 = MissionManager.processEvent index st5 (LogParser.ObjectiveStateChanged (mkTime 5, missionId, "pickup_0", ObjectiveStatus.Completed))
        Assert.Equal(16, st6.CurrentCargoScu)

        // Dropoff complete -> SCU decreases
        let st7 = MissionManager.processEvent index st6 (LogParser.ObjectiveStateChanged (mkTime 6, missionId, "dropoff_0", ObjectiveStatus.Completed))
        Assert.Equal(0, st7.CurrentCargoScu)

    [<Fact>]
    let ``Idempotent SCU Completion on Duplicate Event`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.ContractAccepted (mkTime 0, missionId, "Haul Test"))
        let st2 = MissionManager.processEvent index st1 (LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Gen", "HaulCargo", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=0.;Y=0.;Z=0.}))
        let st3 = MissionManager.processEvent index st2 (LogParser.NewObjective (mkTime 2, missionId, Some "pickup_0", Some Pickup, Some 0, Some 16, Some "Medical", Some "Origin"))
        
        let st4 = MissionManager.processEvent index st3 (LogParser.ObjectiveStateChanged (mkTime 3, missionId, "pickup_0", ObjectiveStatus.Completed))
        Assert.Equal(16, st4.CurrentCargoScu)

        // Duplicate event for pickup_0 Completed should NOT increase cargo twice!
        let st5 = MissionManager.processEvent index st4 (LogParser.ObjectiveStateChanged (mkTime 4, missionId, "pickup_0", ObjectiveStatus.Completed))
        Assert.Equal(16, st5.CurrentCargoScu)

    [<Fact>]
    let ``Courier Multiple Dropoffs Destination Enrichment`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.ContractAccepted (mkTime 0, missionId, "Multi Courier"))
        let st2 = MissionManager.processEvent index st1 (LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Gen", "FTL_Courier", Guid.NewGuid(), "dropoff_0", Dropoff, 1UL, {X=0.;Y=0.;Z=0.}))
        let st3 = MissionManager.processEvent index st2 (LogParser.ObjectiveMarkerCreated (mkTime 2, missionId, "Gen", "FTL_Courier", Guid.NewGuid(), "dropoff_1", Dropoff, 2UL, {X=1.;Y=1.;Z=1.}))

        let st4 = MissionManager.processEvent index st3 (LogParser.NewObjective (mkTime 3, missionId, None, Some Dropoff, None, None, Some "Box", Some "Loc A"))
        let st5 = MissionManager.processEvent index st4 (LogParser.NewObjective (mkTime 4, missionId, None, Some Dropoff, None, None, Some "Box", Some "Loc B"))

        let m = st5.Missions.[missionId]
        let obj0 = m.Objectives |> List.find (fun o -> o.ObjectiveId = "dropoff_0")
        let obj1 = m.Objectives |> List.find (fun o -> o.ObjectiveId = "dropoff_1")

        Assert.Equal(Some "Loc A", obj0.DestinationName)
        Assert.Equal(Some "Loc B", obj1.DestinationName)

    [<Fact>]
    let ``Contract Abandoned or Failed Recalculates Cargo`` () =
        let index = createEmptyIndex()
        let missionId = Guid.NewGuid()
        let st1 = MissionManager.processEvent index MissionManager.initialState (LogParser.ContractAccepted (mkTime 0, missionId, "Haul Test"))
        let st2 = MissionManager.processEvent index st1 (LogParser.ObjectiveMarkerCreated (mkTime 1, missionId, "Gen", "HaulCargo", Guid.NewGuid(), "pickup_0", Pickup, 1UL, {X=0.;Y=0.;Z=0.}))
        let st3 = MissionManager.processEvent index st2 (LogParser.NewObjective (mkTime 2, missionId, Some "pickup_0", Some Pickup, Some 0, Some 20, Some "Ore", Some "Origin"))
        let st4 = MissionManager.processEvent index st3 (LogParser.ObjectiveStateChanged (mkTime 3, missionId, "pickup_0", ObjectiveStatus.Completed))
        Assert.Equal(20, st4.CurrentCargoScu)

        // Abandon contract -> cargo recalculated to 0
        let st5 = MissionManager.processEvent index st4 (LogParser.ContractAbandoned (mkTime 4, missionId, "Haul Test"))
        Assert.Equal(MissionStatus.Abandoned, st5.Missions.[missionId].Status)
        Assert.Equal(0, st5.CurrentCargoScu)

    [<Fact>]
    let ``Integration Game log processes correctly`` () =
        let index = LocationResolver.loadIndex (System.IO.Path.Combine(LogParserTests.findLogsDir(), "..", "locations-positions.json"))
        let path = System.IO.Path.Combine(LogParserTests.findLogsDir(), "Game.log")
        if System.IO.File.Exists(path) then
            let lines = System.IO.File.ReadAllLines(path)
            let parsed = lines |> Array.choose (fun line -> LogParser.parseLine line)
            
            let mutable state = MissionManager.initialState
            for ev in parsed do
                state <- MissionManager.processEvent index state ev

            Assert.True(state.Missions.Count > 0)
