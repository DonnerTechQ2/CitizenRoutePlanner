namespace CitizenRoutePlanner.Core

open System

module RouteEngine =

    type TravelType =
        | SameSurface           // Нет QT, полёт в атмосфере (~150 сек)
        | SameBodyQT            // QT в атмосфере одной планеты/луны (~65 сек)
        | SamePlanetMoonsQT     // QT между спутниками или планета↔спутник (~65 сек)
        | InterplanetaryQT      // QT между разными планетами (~180 сек средн.)

    let private getAtmoExitPenalty (loc: LocationInfo) =
        let n = loc.Name.ToLowerInvariant()
        let isPlanet = loc.Type = "Planet" || n.Contains("microtech") || n.Contains("hurston") || n.Contains("arccorp") || n.Contains("crusader")
        if isPlanet then
            if n.Contains("crusader") then 120.0
            else 90.0
        elif loc.Type = "Moon" then 30.0
        else 0.0 // Station, DC, Outpost, etc. (Usually you are at a POI which inherits parent type or is itself on a body, but we assume the jump starts FROM the body if we are on surface). Wait, LocationInfo for an outpost will have Type="Outpost". 
        // We need to check if the parent is a planet/moon, or just if the location is not in space.
        // Let's refine this below.

    let estimateTravelTime (fromLoc: LocationInfo) (toLoc: LocationInfo) (locations: LocationIndex) (speedMod: float) : float =
        if fromLoc.Uuid = toLoc.Uuid then 0.0
        else
            let fromBody = LocationResolver.getParentBody fromLoc locations
            let toBody = LocationResolver.getParentBody toLoc locations
            
            // Atmo exit penalty. If fromLoc is on a body, we use the body to determine penalty.
            // If fromLoc is already a planet/moon, use it. Otherwise use fromBody.
            let atmoSource = defaultArg fromBody fromLoc
            let n = atmoSource.Name.ToLowerInvariant()
            let isPlanet = atmoSource.Type = "Planet" || n.Contains("microtech") || n.Contains("hurston") || n.Contains("arccorp") || n.Contains("crusader")
            
            // Space stations are in space, no atmo.
            let isFromSpaceStation = fromLoc.Type = "Station" || fromLoc.Type = "SpaceStation" || fromLoc.Name.Contains("Station") || fromLoc.Name.Contains("Port ") || fromLoc.Name.Contains(" Baijini") || fromLoc.Name.Contains("Everus") || fromLoc.Name.Contains("Seraphim")
            
            let atmoExit = 
                if isFromSpaceStation then 0.0
                elif isPlanet then
                    if n.Contains("crusader") then 120.0 else 90.0
                elif atmoSource.Type = "Moon" then 30.0
                else 0.0

            let mutable baseTime = 0.0
            let mutable addSpool = false

            if fromBody = toBody && fromBody.IsSome then
                let distance = LocationResolver.euclideanDistance fromLoc.Position toLoc.Position
                if distance < 20_000.0 then 
                    baseTime <- 150.0       // SameSurface
                else 
                    baseTime <- 65.0        // SameBodyQT
                    addSpool <- true
            elif fromBody.IsSome && toBody.IsSome && LocationResolver.sharePlanet fromBody.Value toBody.Value locations then
                baseTime <- 65.0            // SamePlanetMoonsQT
                addSpool <- true
            else
                // InterplanetaryQT
                let dist = 
                    match fromBody, toBody with
                    | Some fb, Some tb -> LocationResolver.euclideanDistance fb.Position tb.Position
                    | _ -> LocationResolver.euclideanDistance fromLoc.Position toLoc.Position
                baseTime <- 100.0 + dist / 300_000_000.0
                addSpool <- true

            let totalTravel = (baseTime + atmoExit) / max 0.1 speedMod
            if addSpool then totalTravel + 6.0 else totalTravel

    // Внутренние типы для алгоритма
    type private RouteNode = {
        Location: LocationInfo
        Action: RouteAction
        IsPickup: bool
        MissionId: Guid
        Scu: int
    }

    let private getActionLocation (action: RouteAction) (appState: AppState) : LocationInfo option =
        let missionId, objId =
            match action with
            | PickupCargo (m, o, _, _) -> m, o
            | DropoffCargo (m, o, _, _) -> m, o
            | PickupPackage (m, o) -> m, o
            | DropoffPackage (m, o) -> m, o
            | NavTo (m, o) -> m, o
        
        appState.Missions.TryFind missionId
        |> Option.bind (fun m -> m.Objectives |> List.tryFind (fun o -> o.ObjectiveId = objId))
        |> Option.bind (fun obj -> obj.ResolvedLocation)

    let private createUnknownLocation (destNameOpt: string option) (zoneHostId: uint64) =
        let uniqueString = destNameOpt |> Option.defaultValue (sprintf "Unknown Location (%d)" zoneHostId)
        let hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(uniqueString))
        
        let displayName = destNameOpt |> Option.defaultValue "Unknown Location"
        {
            Uuid = Guid(hashBytes)
            Name = displayName
            Type = "Point of Interest"
            System = "Stanton"
            ParentUuid = None
            QtValid = false
            Position = {X=0.; Y=0.; Z=0.}
        }

    let private buildNodes (appState: AppState) : RouteNode list =
        appState.Missions.Values
        |> Seq.filter (fun m -> m.Status = Active)
        |> Seq.collect (fun m ->
            m.Objectives
            |> Seq.filter (fun obj -> obj.Status = Pending || obj.Status = InProgress)
            |> Seq.choose (fun obj ->
                let loc = 
                    match obj.ResolvedLocation with
                    | Some l -> l
                    | None -> createUnknownLocation obj.DestinationName obj.ZoneHostId

                let isPickup = obj.Type = Pickup
                let scu = obj.ScuAmount |> Option.defaultValue 0
                let action = 
                    match m.MissionType, obj.Type with
                    | Courier, Pickup -> PickupPackage (m.MissionId, obj.ObjectiveId)
                    | Courier, Dropoff -> DropoffPackage (m.MissionId, obj.ObjectiveId)
                    | Courier, Nav -> NavTo (m.MissionId, obj.ObjectiveId)
                    | _, Pickup -> PickupCargo (m.MissionId, obj.ObjectiveId, obj.ScuAmount, obj.CargoType)
                    | _, Dropoff -> DropoffCargo (m.MissionId, obj.ObjectiveId, obj.ScuAmount, obj.CargoType)
                    | _, Nav -> NavTo (m.MissionId, obj.ObjectiveId)
                
                Some {
                    Location = loc
                    Action = action
                    IsPickup = isPickup
                    MissionId = m.MissionId
                    Scu = scu
                }
            )
        )
        |> Seq.toList

    let private isValidRoute (nodes: RouteNode list) (capacity: int) (currentCargo: int) : bool =
        let mutable cargo = currentCargo
        let mutable valid = true
        let pickedUp = System.Collections.Generic.HashSet<Guid>()
        
        for node in nodes do
            if valid then
                if node.IsPickup then
                    pickedUp.Add(node.MissionId) |> ignore
                    cargo <- cargo + node.Scu
                    if cargo > capacity then valid <- false
                else
                    // Dropoff without pickup? (Assuming we might already have it if not in pending pickups)
                    cargo <- cargo - node.Scu
                    if cargo < 0 then cargo <- 0 // Just in case
        valid

    let private estimateActionTime (loc: LocationInfo) (actions: RouteAction list) : float =
        if List.isEmpty actions then 0.0
        else
            let isDC = loc.Name.Contains("Distribution Center") || loc.Name.Contains("Inventory Center")
            let isSpaceStation = loc.Type = "Station" || loc.Type = "SpaceStation" || loc.Name.Contains("Station") || loc.Name.Contains("Port ") || loc.Name.Contains(" Baijini") || loc.Name.Contains("Everus") || loc.Name.Contains("Seraphim")
            let isOutpost = loc.Type = "Outpost" || loc.Type = "Mining" || loc.Name.Contains("Outpost") || loc.Name.Contains("Shelter")

            let mutable baseApproach = 0.0
            let mutable actionTime = 0.0

            if isSpaceStation then
                baseApproach <- 90.0
                actionTime <- 120.0
            elif isDC then
                baseApproach <- 75.0
                if loc.Name.Contains("Inventory Center") then actionTime <- 120.0
                else actionTime <- 60.0
            elif isOutpost then
                baseApproach <- 75.0
                actionTime <- 60.0
            else
                baseApproach <- 60.0
                actionTime <- 60.0

            let mutable cargoLoading = 0.0
            for action in actions do
                match action with
                | PickupCargo (_, _, scuOpt, _) ->
                    cargoLoading <- cargoLoading + (float (defaultArg scuOpt 0) * 5.0)
                | _ -> ()

            baseApproach + actionTime + cargoLoading

    let private groupActionsToStops (nodes: RouteNode list) (startLocOpt: LocationInfo option) (locations: LocationIndex) (speedMod: float) : RouteStop list =
        if List.isEmpty nodes then []
        else
            let mutable currentLoc = startLocOpt
            let stops = ResizeArray<RouteStop>()
            
            // Группировка по локациям идущим подряд
            let mutable i = 0
            while i < nodes.Length do
                let loc = nodes.[i].Location
                let mutable j = i
                let actions = ResizeArray<RouteAction>()
                while j < nodes.Length && nodes.[j].Location.Uuid = loc.Uuid do
                    actions.Add(nodes.[j].Action)
                    j <- j + 1
                
                let time = 
                    match currentLoc with
                    | Some curr -> estimateTravelTime curr loc locations speedMod
                    | None -> 0.0
                
                let actionList = Seq.toList actions
                let actionTime = estimateActionTime loc actionList

                stops.Add({
                    Location = loc
                    Actions = actionList
                    TravelTimeEstimate = time
                    ActionTimeEstimate = actionTime
                })
                
                currentLoc <- Some loc
                i <- j

            Seq.toList stops

    // Branch & Bound для малого числа точек
    let private solveBranchAndBound (nodes: RouteNode list) (startLoc: LocationInfo option) (capacity: int) (currentCargo: int) (locations: LocationIndex) (speedMod: float) =
        let bestRoute = ref None
        let bestCost = ref Double.MaxValue
        let n = nodes.Length
        
        // Предобработка: какие dropoff требуют pickup в этом же наборе
        let requiresPickup = 
            nodes 
            |> List.filter (fun x -> not x.IsPickup)
            |> List.map (fun d -> 
                d.MissionId, nodes |> List.exists (fun p -> p.IsPickup && p.MissionId = d.MissionId))
            |> Map.ofList

        let rec backtrack (currentPath: RouteNode list) (remaining: RouteNode list) (currentCost: float) (lastLoc: LocationInfo option) (cargo: int) (pickedUp: Set<Guid>) =
            if currentCost >= bestCost.Value then () // Prune
            elif List.isEmpty remaining then
                bestCost.Value <- currentCost
                bestRoute.Value <- Some (List.rev currentPath)
            else
                for node in remaining do
                    let isDropoffButNeedsPickup = 
                        not node.IsPickup && 
                        (requiresPickup |> Map.tryFind node.MissionId |> Option.defaultValue false) && 
                        not (Set.contains node.MissionId pickedUp)
                    
                    if not isDropoffButNeedsPickup then
                        let newCargo = if node.IsPickup then cargo + node.Scu else cargo - node.Scu
                        if newCargo <= capacity then
                            let travelTime = 
                                match lastLoc with
                                | Some l -> estimateTravelTime l node.Location locations speedMod
                                | None -> 0.0
                            
                            // Note: Action time is not part of route optimization path cost, but we could add it.
                            // Adding it to cost doesn't change optimization much since action time at a node is constant regardless of path.
                            // But for total time it is important. Let's keep it simple for TSP cost.
                            let nextCost = currentCost + travelTime
                            if nextCost < bestCost.Value then
                                let nextRemaining = remaining |> List.filter (fun x -> x <> node)
                                let nextPickedUp = if node.IsPickup then Set.add node.MissionId pickedUp else pickedUp
                                backtrack (node :: currentPath) nextRemaining nextCost (Some node.Location) newCargo nextPickedUp

        backtrack [] nodes 0.0 startLoc currentCargo Set.empty
        bestRoute.Value |> Option.defaultValue nodes

    // Greedy + 2-opt для большого числа точек
    let private solveGreedy (nodes: RouteNode list) (startLoc: LocationInfo option) (capacity: int) (currentCargo: int) (locations: LocationIndex) (speedMod: float) =
        // Упрощенный жадный алгоритм
        let mutable currentLoc = startLoc
        let mutable cargo = currentCargo
        let mutable remaining = nodes
        let route = ResizeArray<RouteNode>()
        let pickedUp = System.Collections.Generic.HashSet<Guid>()
        
        // Для dropoff проверяем, нужен ли pickup
        let requiresPickup = 
            nodes 
            |> List.filter (fun x -> not x.IsPickup)
            |> List.map (fun d -> 
                d.MissionId, nodes |> List.exists (fun p -> p.IsPickup && p.MissionId = d.MissionId))
            |> Map.ofList

        while not (List.isEmpty remaining) do
            // Найти ближайшую допустимую ноду
            let validNodes = 
                remaining |> List.filter (fun node ->
                    let isDropoffButNeedsPickup = 
                        not node.IsPickup && 
                        (requiresPickup |> Map.tryFind node.MissionId |> Option.defaultValue false) && 
                        not (pickedUp.Contains node.MissionId)
                    
                    let newCargo = if node.IsPickup then cargo + node.Scu else cargo - node.Scu
                    not isDropoffButNeedsPickup && newCargo <= capacity
                )
            
            if List.isEmpty validNodes then
                // Заглушка на случай тупика (например capacity слишком мал)
                let node = List.head remaining
                route.Add(node)
                remaining <- List.tail remaining
            else
                let bestNode = 
                    validNodes |> List.minBy (fun n -> 
                        match currentLoc with
                        | Some l -> estimateTravelTime l n.Location locations speedMod
                        | None -> 0.0)
                
                route.Add(bestNode)
                if bestNode.IsPickup then pickedUp.Add(bestNode.MissionId) |> ignore
                cargo <- if bestNode.IsPickup then cargo + bestNode.Scu else cargo - bestNode.Scu
                if cargo < 0 then cargo <- 0
                currentLoc <- Some bestNode.Location
                remaining <- remaining |> List.filter (fun x -> x <> bestNode)
                
        let greedyRoute = Seq.toList route

        // 2-opt pass
        let mutable currentRoute = Array.ofList greedyRoute
        let mutable improved = true
        let n = currentRoute.Length
        
        let evalRoute (r: RouteNode array) =
            let mutable valid = true
            let mutable c = currentCargo
            let pUp = System.Collections.Generic.HashSet<Guid>()
            let reqPickup = 
                r |> Array.filter (fun x -> not x.IsPickup)
                  |> Array.map (fun d -> d.MissionId, r |> Array.exists (fun p -> p.IsPickup && p.MissionId = d.MissionId))
                  |> Map.ofArray
            let mutable totalTime = 0.0
            let mutable lastLoc = startLoc
            
            for i in 0 .. n - 1 do
                let node = r.[i]
                if valid then
                    let isDropoffButNeedsPickup = 
                        not node.IsPickup && 
                        (reqPickup |> Map.tryFind node.MissionId |> Option.defaultValue false) && 
                        not (pUp.Contains node.MissionId)
                    
                    if isDropoffButNeedsPickup then valid <- false
                    else
                        if node.IsPickup then pUp.Add(node.MissionId) |> ignore
                        c <- if node.IsPickup then c + node.Scu else c - node.Scu
                        if c < 0 then c <- 0
                        if c > capacity then valid <- false
                        else
                            let time = match lastLoc with | Some l -> estimateTravelTime l node.Location locations speedMod | None -> 0.0
                            totalTime <- totalTime + time
                            lastLoc <- Some node.Location
            if valid then Some totalTime else None

        let mutable bestCost = evalRoute currentRoute |> Option.defaultValue Double.MaxValue

        while improved do
            improved <- false
            for i in 0 .. n - 2 do
                for j in i + 1 .. n - 1 do
                    if not improved then
                        let newRoute = Array.copy currentRoute
                        for k in 0 .. (j - i) / 2 do
                            let tmp = newRoute.[i + k]
                            newRoute.[i + k] <- newRoute.[j - k]
                            newRoute.[j - k] <- tmp
                        
                        match evalRoute newRoute with
                        | Some cost when cost < bestCost - 0.1 -> // prevent float precision loop
                            bestCost <- cost
                            currentRoute <- newRoute
                            improved <- true
                        | _ -> ()

        Array.toList currentRoute

    let calculateRoute (appState: AppState) (locations: LocationIndex) : Route option =
        let nodes = buildNodes appState
        if List.isEmpty nodes then None
        else
            let startLoc = appState.PlayerLocation // В реальности может быть QuantumDestination
            let speedMod = appState.ShipSpeedModifier
            let optimizedNodes = 
                if nodes.Length <= 12 then
                    solveBranchAndBound nodes startLoc appState.ShipCapacityScu appState.CurrentCargoScu locations speedMod
                else
                    solveGreedy nodes startLoc appState.ShipCapacityScu appState.CurrentCargoScu locations speedMod
            
            let stops = groupActionsToStops optimizedNodes startLoc locations speedMod
            let totalTime = stops |> List.sumBy (fun s -> s.TravelTimeEstimate + s.ActionTimeEstimate)
            
            Some {
                Stops = stops
                TotalEstimatedTime = totalTime
                CurrentStopIndex = 0
            }
