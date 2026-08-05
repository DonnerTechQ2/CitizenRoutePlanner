namespace CitizenRoutePlanner.Tests

open System
open Xunit
open CitizenRoutePlanner.Core

module LogParserTests =

    [<Fact>]
    let ``Parse Contract Accepted`` () =
        let line = """Added notification "Contract Accepted:  JR. LVL. COURIER NEEDED IN STANTON: " [10] to queue. New queue size: 1, MissionId: [f2a4d319-9c55-48f6-bee0-89d9c10d8622]"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseContractAccepted line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.ContractAccepted(_, id, title) ->
            Assert.Equal("f2a4d319-9c55-48f6-bee0-89d9c10d8622", id.ToString())
            Assert.Equal("JR. LVL. COURIER NEEDED IN STANTON", title)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse Contract Complete`` () =
        let line = """Contract Complete: JR. LVL. COURIER NEEDED IN STANTON: " [10] to queue. New queue size: 1, MissionId: [f2a4d319-9c55-48f6-bee0-89d9c10d8622]"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseContractCompleted line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.ContractCompleted(_, id, title) ->
            Assert.Equal("f2a4d319-9c55-48f6-bee0-89d9c10d8622", id.ToString())
            Assert.Equal("JR. LVL. COURIER NEEDED IN STANTON", title)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse Objective Marker Created`` () =
        let line = """Creating objective marker: missionId [b4f0b3f8-6e5a-4e67-9e79-8d19d690a2a4], generator name [Covalex_Hauling], contract [HaulCargo_AToB_RefinedOre_Corundum_Stanton_SmallGrade1], contractDefinitionId[8c9f6974-bc5a-4c12-8e11-e40f6f3d9d37], objectiveId [pickup_0], markerEntityId [123], zoneHostId [456], position [x: 100.5, y: -200.0, z: 300.0]"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseObjectiveMarker line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.ObjectiveMarkerCreated(_, mId, gName, cName, cDefId, oId, oType, zId, pos) ->
            Assert.Equal("b4f0b3f8-6e5a-4e67-9e79-8d19d690a2a4", mId.ToString())
            Assert.Equal(Pickup, oType)
            Assert.Equal("pickup_0", oId)
            Assert.Equal(456UL, zId)
            Assert.Equal(100.5, pos.X)
            Assert.Equal(-200.0, pos.Y)
            Assert.Equal(300.0, pos.Z)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse New Objective with SCU`` () =
        let line = """New Objective: Deliver 0/8 SCU of Processed_Mixed to ArcCorp Mining Area 141: " [12] to queue. New queue size: 1, MissionId: [f2a4d319-9c55-48f6-bee0-89d9c10d8622]"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseNewObjective line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.NewObjective(_, mId, objId, typeHint, scuCur, scuTot, cargoType, destName) ->
            Assert.Equal("f2a4d319-9c55-48f6-bee0-89d9c10d8622", mId.ToString())
            Assert.Equal(Some Dropoff, typeHint)
            Assert.Equal(Some 8, scuTot)
            Assert.Equal(Some "Processed_Mixed", cargoType)
            Assert.Equal(Some "ArcCorp Mining Area 141", destName)
            Assert.Equal(None, objId)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse New Objective Courier Collect`` () =
        let line = """<2026-08-05T03:37:02.600Z> [Notice] <SHUDEvent_OnNotification> Added notification "New Objective: Collect Chlorine From wreck site near MicroTech: " [20] to queue. New queue size: 2, MissionId: [e4cbdc40-ecc6-41b3-8462-038d9963602a], ObjectiveId: [pickup_c11891c4-4476-4192-be6e-0f5f43c7fc22_0]"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseNewObjective line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.NewObjective(_, mId, objId, typeHint, scuCur, scuTot, cargoType, destName) ->
            Assert.Equal("e4cbdc40-ecc6-41b3-8462-038d9963602a", mId.ToString())
            Assert.Equal(Some Pickup, typeHint)
            Assert.Equal(None, scuTot)
            Assert.Equal(Some "Chlorine", cargoType)
            Assert.Equal(Some "wreck site near MicroTech", destName)
            Assert.Equal(Some "pickup_c11891c4-4476-4192-be6e-0f5f43c7fc22_0", objId)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse New Objective Courier Deliver`` () =
        let line = """<2026-08-05T03:37:02.600Z> [Notice] <SHUDEvent_OnNotification> Added notification "New Objective: Deliver Chlorine To a Landing Pad Locker in New Babbage: " [21] to queue. New queue size: 3, MissionId: [e4cbdc40-ecc6-41b3-8462-038d9963602a], ObjectiveId: [dropoff_c11891c4-4476-4192-be6e-0f5f43c7fc22_0]"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseNewObjective line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.NewObjective(_, mId, objId, typeHint, scuCur, scuTot, cargoType, destName) ->
            Assert.Equal("e4cbdc40-ecc6-41b3-8462-038d9963602a", mId.ToString())
            Assert.Equal(Some Dropoff, typeHint)
            Assert.Equal(None, scuTot)
            Assert.Equal(Some "Chlorine", cargoType)
            Assert.Equal(Some "a Landing Pad Locker in New Babbage", destName)
            Assert.Equal(Some "dropoff_c11891c4-4476-4192-be6e-0f5f43c7fc22_0", objId)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Determine Mission Type`` () =
        Assert.Equal(Courier, LogParser.determineMissionType "FTL_Courier_Stanton_TissueSamples_Rank1_2")
        Assert.Equal(DirectHaul, LogParser.determineMissionType "HaulCargo_AToB_RefinedOre_Corundum_Stanton_SmallGrade1")
        Assert.Equal(MultiHaul 2, LogParser.determineMissionType "HaulCargo_SingleToMulti2_Processed")
        Assert.Equal(MultiHaul 4, LogParser.determineMissionType "HaulCargo_SingleToMulti4_RefinedOre")
        Assert.Equal(MultiHaul 4, LogParser.determineMissionType "HaulCargo_Multi4ToSingle")
        Assert.Equal(MultiHaul 3, LogParser.determineMissionType "HaulCargo_Multi3ToSingle")
        Assert.Equal(MultiHaul 2, LogParser.determineMissionType "Foxwell_PAF_HaulCargo_Multi2ToSingle")
        Assert.Equal(DirectHaul, LogParser.determineMissionType "Foxwell_PAF_HaulCargo_AtoB")
        Assert.Equal(DirectHaul, LogParser.determineMissionType "RedWind_Pyro_BulkGrade_1")
        Assert.Equal(DirectHaul, LogParser.determineMissionType "Redwind_ASD_Medical Supplies")
        Assert.Equal(DirectHaul, LogParser.determineMissionType "Redwind_ASD_EletronicEquipment")
        Assert.Equal(Courier, LogParser.determineMissionType "Covalex_DeliveryPilot_Stanton_1")
        Assert.Equal(Courier, LogParser.determineMissionType "Hockrow_BlackBoxRecovery_1")
        Assert.Equal(Courier, LogParser.determineMissionType "BitZeros_BlackBoxRecovery_")

    [<Fact>]
    let ``Parse Objective Upserted`` () =
        let line = """<2024-05-18T19:35:48.337Z> [Notice] <ObjectiveUpserted> mission_id 12345678-1234-1234-1234-123456789abc - objective_id dropoff_0 - state MISSION_OBJECTIVE_STATE_COMPLETED"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseObjectiveUpserted line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.ObjectiveStateChanged(_, mId, objId, state) ->
            Assert.Equal("12345678-1234-1234-1234-123456789abc", mId.ToString())
            Assert.Equal("dropoff_0", objId)
            Assert.Equal(ObjectiveStatus.Completed, state)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse Quantum Route Calculated`` () =
        let line = """<2024-05-18T19:35:48.337Z> CalculateRoute|Projected Start Location is microTech for route to destination Hurston"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseQuantumRouteCalculated line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.QuantumRouteCalculated(_, startLoc, dest) ->
            Assert.Equal("microTech", startLoc)
            Assert.Equal("Hurston", dest)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Parse Quantum Arrived`` () =
        let line = """<2024-05-18T19:35:48.337Z> Quantum Drive has arrived at final destination"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseQuantumArrived line ts
        Assert.True(result.IsSome)
        
    [<Fact>]
    let ``Parse Item Registered`` () =
        let line = """<2024-05-18T19:35:48.337Z> Mission Item Box (123) registered with mission id 12345678-1234-1234-1234-123456789abc, phase id phase1, pickup objective id pickup_0, drop off objective id dropoff_0"""
        let ts = DateTimeOffset.UtcNow
        let result = LogParser.parseItemRegistered line ts
        Assert.True(result.IsSome)
        match result.Value with
        | LogParser.LogEvent.ItemRegistered(_, mId, pId, dId, name) ->
            Assert.Equal("12345678-1234-1234-1234-123456789abc", mId.ToString())
            Assert.Equal("pickup_0", pId)
            Assert.Equal("dropoff_0", dId)
            Assert.Equal("Box", name)
        | _ -> Assert.Fail("Wrong event type")

    [<Fact>]
    let ``Determine Mission Scope`` () =
        Assert.Equal(System, LogParser.determineMissionScope "HaulCargo_AToB_RefinedOre_Corundum_Stanton_SmallGrade1")
        Assert.Equal(Local 1, LogParser.determineMissionScope "HaulCargo_AToB_RefinedOre_Corundum_Stanton1_SmallGrade1")
        Assert.Equal(Local 2, LogParser.determineMissionScope "HaulCargo_AToB_RefinedOre_Corundum_Stanton2_SmallGrade1")

    let findLogsDir () =
        let rec search dir =
            if System.IO.Directory.Exists(System.IO.Path.Combine(dir, "logs")) then
                System.IO.Path.Combine(dir, "logs")
            else
                let parent = System.IO.Directory.GetParent(dir)
                if parent = null then failwith "logs dir not found"
                else search parent.FullName
        search System.AppContext.BaseDirectory

    [<Fact>]
    let ``Integration Game log parses correctly`` () =
        let path = System.IO.Path.Combine(findLogsDir(), "Game.log")
        if System.IO.File.Exists(path) then
            let lines = System.IO.File.ReadAllLines(path)
            let parsed = lines |> Array.choose (fun line -> LogParser.parseLine line)
            Assert.Equal(268, parsed.Length)

    [<Fact>]
    let ``Integration Game2 log parses correctly`` () =
        let path = System.IO.Path.Combine(findLogsDir(), "Game2.log")
        if System.IO.File.Exists(path) then
            let lines = System.IO.File.ReadAllLines(path)
            let parsed = lines |> Array.choose (fun line -> LogParser.parseLine line)
            Assert.Equal(26, parsed.Length)
