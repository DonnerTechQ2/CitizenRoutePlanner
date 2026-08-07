namespace CitizenRoutePlanner.Core

open System

module MissionManager =

    let initialState : AppState = {
        Missions = Map.empty
        CurrentRoute = None
        PlayerLocation = None
        QuantumDestination = None
        Ship = None
        CurrentCargoScu = 0
        QuantumDrive = None
    }

    let recalculateCargoScu (missions: Map<Guid, Mission>) =
        let completedObjs =
            missions.Values
            |> Seq.filter (fun m -> m.Status = MissionStatus.Active)
            |> Seq.collect (fun m -> m.Objectives)
            |> Seq.filter (fun o -> o.Status = ObjectiveStatus.Completed)

        let pickedUp = completedObjs |> Seq.filter (fun o -> o.Type = Pickup) |> Seq.choose (fun o -> o.ScuAmount) |> Seq.sum
        let droppedOff = completedObjs |> Seq.filter (fun o -> o.Type = Dropoff) |> Seq.choose (fun o -> o.ScuAmount) |> Seq.sum
        
        Math.Max(0, pickedUp - droppedOff)

    let extractSuffix (id: string) =
        let lower = id.ToLowerInvariant()
        if lower.StartsWith("dropoff_") then id.Substring(8)
        elif lower.StartsWith("pickup_") then id.Substring(7)
        elif lower.StartsWith("dropoff") then id.Substring(7)
        elif lower.StartsWith("pickup") then id.Substring(6)
        else id

    let syncObjectivesWithContract (contractNameOpt: string option) (objs: MissionObjective list) : MissionObjective list =
        let fallbackOpt = contractNameOpt |> Option.bind LogParser.extractCargoTypeFromContractName

        let isGeneric (c: string) =
            match fallbackOpt with
            | Some fb when String.Equals(c, fb, StringComparison.OrdinalIgnoreCase) -> true
            | _ -> false

        let pickups = objs |> List.filter (fun o -> o.Type = Pickup)
        let dropoffs = objs |> List.filter (fun o -> o.Type = Dropoff)

        objs |> List.map (fun obj ->
            match obj.Type with
            | Pickup ->
                let assocDropoffs =
                    if pickups.Length = 1 then
                        dropoffs
                    else
                        let matched =
                            dropoffs |> List.filter (fun d ->
                                (obj.PairedObjectiveId.IsSome && obj.PairedObjectiveId.Value = d.ObjectiveId) ||
                                (d.PairedObjectiveId.IsSome && d.PairedObjectiveId.Value = obj.ObjectiveId) ||
                                (let s = extractSuffix obj.ObjectiveId in s <> "" && s = extractSuffix d.ObjectiveId)
                            )
                        if List.isEmpty matched then dropoffs else matched

                let dropoffScus = assocDropoffs |> List.choose (fun d -> d.ScuAmount)
                let totalScu =
                    if pickups.Length = 1 then
                        if not (List.isEmpty dropoffScus) && List.sum dropoffScus > 0 then
                            Some (List.sum dropoffScus)
                        else
                            obj.ScuAmount
                    else
                        match obj.ScuAmount with
                        | Some s when s > 0 -> Some s
                        | _ ->
                            if not (List.isEmpty dropoffScus) && List.sum dropoffScus > 0 then
                                Some (List.sum dropoffScus)
                            else
                                None

                let dropoffCargos = assocDropoffs |> List.choose (fun d -> d.CargoType) |> List.filter (fun c -> not (String.IsNullOrWhiteSpace c))
                let specificCargos = dropoffCargos |> List.filter (fun c -> not (isGeneric c))
                let cargosToUse = if not (List.isEmpty specificCargos) then specificCargos else dropoffCargos
                let distinctCargos = cargosToUse |> List.distinct

                let combinedCargo =
                    if not (List.isEmpty distinctCargos) then
                        Some (String.Join(", ", distinctCargos))
                    else
                        obj.CargoType |> Option.orElse fallbackOpt

                let pairedId =
                    if assocDropoffs.Length = 1 then Some assocDropoffs.Head.ObjectiveId
                    else obj.PairedObjectiveId

                { obj with
                    ScuAmount = totalScu
                    CargoType = combinedCargo
                    PairedObjectiveId = pairedId }

            | Dropoff ->
                let assocPickups =
                    if dropoffs.Length = 1 then
                        pickups
                    else
                        let matched =
                            pickups |> List.filter (fun p ->
                                (obj.PairedObjectiveId.IsSome && obj.PairedObjectiveId.Value = p.ObjectiveId) ||
                                (p.PairedObjectiveId.IsSome && p.PairedObjectiveId.Value = obj.ObjectiveId) ||
                                (let s = extractSuffix obj.ObjectiveId in s <> "" && s = extractSuffix p.ObjectiveId)
                            )
                        if List.isEmpty matched then pickups else matched

                let pickupScus = assocPickups |> List.choose (fun p -> p.ScuAmount)
                let totalScu =
                    if dropoffs.Length = 1 then
                        if not (List.isEmpty pickupScus) && List.sum pickupScus > 0 then
                            Some (List.sum pickupScus)
                        else
                            obj.ScuAmount
                    else
                        match obj.ScuAmount with
                        | Some s when s > 0 -> Some s
                        | _ ->
                            if not (List.isEmpty pickupScus) && List.sum pickupScus > 0 then
                                Some (List.sum pickupScus)
                            else
                                None

                let pairedId =
                    if assocPickups.Length = 1 then Some assocPickups.Head.ObjectiveId
                    else obj.PairedObjectiveId

                let cargo =
                    match obj.CargoType with
                    | None ->
                        if assocPickups.Length = 1 then assocPickups.Head.CargoType else None
                    | Some c when isGeneric c ->
                        let pickupCargo = if assocPickups.Length = 1 then assocPickups.Head.CargoType else None
                        match pickupCargo with
                        | Some pC when not (isGeneric pC) -> Some pC
                        | _ -> obj.CargoType
                    | _ -> obj.CargoType

                { obj with
                    ScuAmount = totalScu
                    CargoType = cargo
                    PairedObjectiveId = pairedId }

            | Nav -> obj
        )

    let syncObjectives (objs: MissionObjective list) = syncObjectivesWithContract None objs

    let processEvent (index: LocationIndex) (state: AppState) (event: LogParser.LogEvent) : AppState =
        match event with
        | LogParser.ContractAccepted (ts, missionId, title) ->
            let mission = 
                match Map.tryFind missionId state.Missions with
                | Some existing ->
                    { existing with 
                        Title = if String.IsNullOrWhiteSpace existing.Title || existing.Title = "Unknown Mission" then title else existing.Title
                        AcceptedAt = ts }
                | None ->
                    {
                        MissionId = missionId
                        Title = title
                        GeneratorName = ""
                        ContractName = ""
                        ContractDefinitionId = Guid.Empty
                        MissionType = Courier
                        Scope = System
                        Objectives = []
                        PendingObjectivesData = []
                        Status = Active
                        AcceptedAt = ts
                    }
            { state with Missions = Map.add missionId mission state.Missions }

        | LogParser.ObjectiveMarkerCreated (ts, missionId, genName, contractName, contractDefId, objId, objType, zoneHostId, pos) ->
            let mission = 
                match Map.tryFind missionId state.Missions with
                | Some m -> m
                | None ->
                    { MissionId = missionId
                      Title = "Unknown Mission"
                      GeneratorName = genName
                      ContractName = contractName
                      ContractDefinitionId = contractDefId
                      MissionType = LogParser.determineMissionType contractName
                      Scope = LogParser.determineMissionScope contractName
                      Objectives = []
                      PendingObjectivesData = []
                      Status = Active
                      AcceptedAt = ts }
            
            let updatedMission =
                { mission with 
                    GeneratorName = if mission.GeneratorName = "" then genName else mission.GeneratorName
                    ContractName = if mission.ContractName = "" then contractName else mission.ContractName
                    ContractDefinitionId = if mission.ContractDefinitionId = Guid.Empty then contractDefId else mission.ContractDefinitionId
                    MissionType = LogParser.determineMissionType contractName
                    Scope = LogParser.determineMissionScope contractName
                }

            let existingObjOpt = updatedMission.Objectives |> List.tryFind (fun o -> o.ObjectiveId = objId)

            let pendingScu, pendingCargo, pendingDest, newPendingData =
                match existingObjOpt with
                | None ->
                    let matchingPending = 
                        updatedMission.PendingObjectivesData 
                        |> List.tryFind (fun (typeHintOpt, _, _, _) -> 
                            match typeHintOpt with
                            | Some hint -> hint = objType
                            | None -> true
                        )
                    match matchingPending with
                    | Some item ->
                        let rec removeFirst item list =
                            match list with
                            | [] -> []
                            | h :: t when h = item -> t
                            | h :: t -> h :: removeFirst item t
                        let remaining = removeFirst item updatedMission.PendingObjectivesData
                        let (_, s, c, d) = item
                        let destForThisObj = if objType = Dropoff then d else None
                        s, c, destForThisObj, remaining
                    | None -> None, None, None, updatedMission.PendingObjectivesData
                | Some _ -> None, None, None, updatedMission.PendingObjectivesData

            let updatedMission2 = { updatedMission with PendingObjectivesData = newPendingData }

            let markerInfo : MarkerInfo = { Position = pos; ZoneHostId = zoneHostId }
            let resolved = LocationResolver.resolveLocation index pendingDest (Some markerInfo)

            let absPosOpt = 
                match resolved with
                | KnownLocation (_, p) -> Some p
                | InferredLocation (_, p, _) -> Some p
                | UnknownLocation _ -> None
            
            let locInfoOpt = 
                match resolved with
                | KnownLocation (l, _) -> Some l
                | InferredLocation (l, _, _) -> Some l
                | UnknownLocation _ -> None

            match existingObjOpt with
            | Some existingObj ->
                let destName = existingObj.DestinationName
                let resolvedWithDest = LocationResolver.resolveLocation index destName (Some markerInfo)

                let absPosOptExisting = 
                    match resolvedWithDest with
                    | KnownLocation (_, p) -> Some p
                    | InferredLocation (_, p, _) -> Some p
                    | UnknownLocation _ -> None
                
                let locInfoOptExisting = 
                    match resolvedWithDest with
                    | KnownLocation (l, _) -> Some l
                    | InferredLocation (l, _, _) -> Some l
                    | UnknownLocation _ -> None

                let newObj = { existingObj with 
                                Type = existingObj.Type
                                RawPosition = pos
                                ZoneHostId = zoneHostId
                                AbsolutePosition = absPosOptExisting |> Option.orElse existingObj.AbsolutePosition
                                ResolvedLocation = locInfoOptExisting |> Option.orElse existingObj.ResolvedLocation }
                let newObjs = updatedMission2.Objectives |> List.map (fun o -> if o.ObjectiveId = objId then newObj else o) |> syncObjectivesWithContract (Some updatedMission2.ContractName)
                let m = { updatedMission2 with Objectives = newObjs }
                { state with Missions = Map.add missionId m state.Missions }
            | None ->
                let newObj = {
                    ObjectiveId = objId
                    Type = objType
                    RawPosition = pos
                    ZoneHostId = zoneHostId
                    AbsolutePosition = absPosOpt
                    ResolvedLocation = locInfoOpt
                    ScuAmount = pendingScu
                    CargoType = pendingCargo |> Option.orElse (LogParser.extractCargoTypeFromContractName contractName)
                    DestinationName = pendingDest
                    Status = Pending
                    PairedObjectiveId = None
                }
                let newObjs = (updatedMission2.Objectives @ [newObj]) |> syncObjectivesWithContract (Some updatedMission2.ContractName)
                let m = { updatedMission2 with Objectives = newObjs }
                { state with Missions = Map.add missionId m state.Missions }

        | LogParser.NewObjective (ts, missionId, objIdOpt, targetObjTypeHint, scuCur, scuTot, cargoType, destName) ->
            let mission =
                match Map.tryFind missionId state.Missions with
                | Some m -> m
                | None ->
                    { MissionId = missionId
                      Title = "Unknown Mission"
                      GeneratorName = ""
                      ContractName = ""
                      ContractDefinitionId = Guid.Empty
                      MissionType = Courier
                      Scope = System
                      Objectives = []
                      PendingObjectivesData = []
                      Status = Active
                      AcceptedAt = ts }

            let targetObjIdOpt =
                match objIdOpt with
                | Some id -> Some id
                | None ->
                    let targetType = targetObjTypeHint |> Option.defaultValue Dropoff
                    let matchByDest =
                        match destName with
                        | Some dName when not (String.IsNullOrWhiteSpace dName) ->
                            mission.Objectives
                            |> List.tryFind (fun o -> 
                                o.Type = targetType && 
                                (
                                    (o.DestinationName.IsSome && String.Equals(o.DestinationName.Value, dName, StringComparison.OrdinalIgnoreCase)) ||
                                    (o.ResolvedLocation.IsSome && String.Equals(o.ResolvedLocation.Value.Name, dName, StringComparison.OrdinalIgnoreCase))
                                )
                            )
                        | _ -> None

                    match matchByDest with
                    | Some matched -> Some matched.ObjectiveId
                    | None ->
                        mission.Objectives 
                        |> List.tryFind (fun o -> o.Type = targetType && o.DestinationName.IsNone)
                        |> Option.map (fun o -> o.ObjectiveId)

            match targetObjIdOpt with
            | Some objId ->
                match mission.Objectives |> List.tryFind (fun o -> o.ObjectiveId = objId) with
                | Some obj ->
                    let markerInfo : MarkerInfo = { Position = obj.RawPosition; ZoneHostId = obj.ZoneHostId }
                    let resolved = LocationResolver.resolveLocation index destName (Some markerInfo)
                    
                    let absPosOpt = 
                        match resolved with
                        | KnownLocation (_, p) -> Some p
                        | InferredLocation (_, p, _) -> Some p
                        | UnknownLocation _ -> obj.AbsolutePosition

                    let locInfoOpt = 
                        match resolved with
                        | KnownLocation (l, _) -> Some l
                        | InferredLocation (l, _, _) -> Some l
                        | UnknownLocation _ -> obj.ResolvedLocation

                    let newObjs = 
                        mission.Objectives |> List.map (fun o -> 
                            if o.ObjectiveId = objId then
                                { o with 
                                    ScuAmount = if scuTot.IsSome then scuTot else o.ScuAmount
                                    CargoType = if cargoType.IsSome then cargoType else o.CargoType
                                    DestinationName = if destName.IsSome then destName else o.DestinationName
                                    AbsolutePosition = absPosOpt
                                    ResolvedLocation = locInfoOpt }
                            else o
                        )
                        |> syncObjectivesWithContract (Some mission.ContractName)

                    let m = { mission with Objectives = newObjs }
                    { state with Missions = Map.add missionId m state.Missions }
                | None ->
                    let resolved = LocationResolver.resolveLocation index destName None
                    let locInfoOpt = 
                        match resolved with
                        | KnownLocation(l, _) -> Some l
                        | InferredLocation(l, _, _) -> Some l
                        | _ -> None

                    let objType = targetObjTypeHint |> Option.defaultValue Dropoff
                    let absPosOpt = 
                        match resolved with
                        | KnownLocation (_, p) -> Some p
                        | InferredLocation (_, p, _) -> Some p
                        | _ -> None
                        
                    let newObj = {
                        ObjectiveId = objId
                        Type = objType 
                        RawPosition = {X=0.; Y=0.; Z=0.}
                        ZoneHostId = 0UL
                        AbsolutePosition = absPosOpt
                        ResolvedLocation = locInfoOpt
                        ScuAmount = scuTot
                        CargoType = cargoType
                        DestinationName = destName
                        Status = Pending
                        PairedObjectiveId = None
                    }
                    let newObjs = (mission.Objectives @ [newObj]) |> syncObjectivesWithContract (Some mission.ContractName)
                    let m = { mission with Objectives = newObjs }
                    { state with Missions = Map.add missionId m state.Missions }
            | None ->
                let m = { mission with PendingObjectivesData = mission.PendingObjectivesData @ [(targetObjTypeHint, scuTot, cargoType, destName)] }
                { state with Missions = Map.add missionId m state.Missions }

        | LogParser.ObjectiveStateChanged (ts, missionId, objId, objStatus) ->
            match Map.tryFind missionId state.Missions with
            | None -> state
            | Some mission ->
                let targetObjOpt = mission.Objectives |> List.tryFind (fun o -> o.ObjectiveId = objId)
                let isNewCompletion = objStatus = ObjectiveStatus.Completed && (targetObjOpt |> Option.exists (fun o -> o.Status <> ObjectiveStatus.Completed))

                let newObjs = 
                    mission.Objectives 
                    |> List.map (fun o -> 
                        if o.ObjectiveId = objId then { o with Status = objStatus }
                        elif isNewCompletion && targetObjOpt.IsSome && targetObjOpt.Value.Type = Pickup && o.Type = Nav then
                            { o with Status = ObjectiveStatus.Completed }
                        else o
                    )
                let m = { mission with Objectives = newObjs }
                let updatedMissions = Map.add missionId m state.Missions

                let updatedCargoScu =
                    if isNewCompletion then
                        match targetObjOpt with
                        | Some targetObj ->
                            match targetObj.Type, targetObj.ScuAmount with
                            | Pickup, Some scu -> state.CurrentCargoScu + scu
                            | Dropoff, Some scu -> Math.Max(0, state.CurrentCargoScu - scu)
                            | _ -> state.CurrentCargoScu
                        | None -> state.CurrentCargoScu
                    else state.CurrentCargoScu

                let updatedPlayerLoc =
                    if objStatus = ObjectiveStatus.Completed then
                        match targetObjOpt with
                        | Some targetObj ->
                            match targetObj.ResolvedLocation with
                            | Some loc -> Some loc
                            | None -> state.PlayerLocation
                        | None -> state.PlayerLocation
                    else state.PlayerLocation

                { state with 
                    Missions = updatedMissions
                    PlayerLocation = updatedPlayerLoc
                    CurrentCargoScu = updatedCargoScu }

        | LogParser.ContractCompleted (ts, missionId, title) ->
            match Map.tryFind missionId state.Missions with
            | None -> state
            | Some mission ->
                let m = { mission with Status = MissionStatus.Completed }
                let updatedMissions = Map.add missionId m state.Missions
                { state with Missions = updatedMissions; CurrentCargoScu = recalculateCargoScu updatedMissions }

        | LogParser.ContractFailed (ts, missionId, title) ->
            match Map.tryFind missionId state.Missions with
            | None -> state
            | Some mission ->
                let m = { mission with Status = MissionStatus.Failed }
                let updatedMissions = Map.add missionId m state.Missions
                { state with Missions = updatedMissions; CurrentCargoScu = recalculateCargoScu updatedMissions }

        | LogParser.ContractAbandoned (ts, missionId, title) ->
            match Map.tryFind missionId state.Missions with
            | None -> state
            | Some mission ->
                let m = { mission with Status = MissionStatus.Abandoned }
                let updatedMissions = Map.add missionId m state.Missions
                { state with Missions = updatedMissions; CurrentCargoScu = recalculateCargoScu updatedMissions }

        | LogParser.QuantumRouteCalculated (ts, startLoc, dest) ->
            let startLower = startLoc.ToLowerInvariant()
            let destLower = dest.ToLowerInvariant()
            let pLoc = match Map.tryFind startLower index.ByName with | Some l -> Some l | None -> state.PlayerLocation
            let qDest = match Map.tryFind destLower index.ByName with | Some l -> Some l | None -> state.QuantumDestination
            { state with PlayerLocation = pLoc; QuantumDestination = qDest }

        | LogParser.QuantumArrived ts ->
            match state.QuantumDestination with
            | Some destLoc -> { state with PlayerLocation = Some destLoc; QuantumDestination = None }
            | None -> state

        | LogParser.ItemRegistered (ts, missionId, pickupObjId, dropoffObjId, itemName) ->
            match Map.tryFind missionId state.Missions with
            | None -> state
            | Some mission ->
                let updatedObjs =
                    mission.Objectives
                    |> List.map (fun o ->
                        if o.ObjectiveId = pickupObjId then
                            { o with PairedObjectiveId = Some dropoffObjId
                                     CargoType = o.CargoType |> Option.orElse (Some itemName) }
                        elif o.ObjectiveId = dropoffObjId then
                            { o with PairedObjectiveId = Some pickupObjId
                                     CargoType = o.CargoType |> Option.orElse (Some itemName) }
                        else o
                    )
                    |> syncObjectivesWithContract (Some mission.ContractName)
                let m = { mission with Objectives = updatedObjs }
                { state with Missions = Map.add missionId m state.Missions }
