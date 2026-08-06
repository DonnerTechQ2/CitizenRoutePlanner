namespace CitizenRoutePlanner.Core

open System

module RouteEngine =

    type TravelType =
        | SameSurface           // Нет QT, полёт в атмосфере (~150 сек)
        | SameBodyQT            // QT в атмосфере одной планеты/луны (~65 сек)
        | SamePlanetMoonsQT     // QT между спутниками или планета↔спутник (~65 сек)
        | InterplanetaryQT      // QT между разными планетами (~180 сек средн.)

    let defaultHemeraDrive = {
        Name = "Hemera"
        Standard = {
            DriveSpeed = 282_000_000.0
            StageOneAccel = 4_200_000.0
            StageTwoAccel = 18_500_000.0
            SpoolUpTime = 6.0
            CooldownTime = 15.66
        }
        Spline = {
            DriveSpeed = 400_000.0
            StageOneAccel = 250.0
            StageTwoAccel = 50_000.0
            SpoolUpTime = 6.0
            CooldownTime = 15.66
        }
    }

    let getPlanetRadius (name: string) (typeStr: string) =
        let n = name.ToLowerInvariant()
        if n.Contains("crusader") then 7_500_000.0
        elif typeStr.Equals("Planet", StringComparison.OrdinalIgnoreCase) then 1_000_000.0
        else 250_000.0

    let calculateKinematicTime (distance: float) (stats: QuantumModeStats) =
        let a_sum = stats.StageOneAccel + stats.StageTwoAccel
        let accelTime = (2.0 * stats.DriveSpeed) / a_sum
        let accelDist = (pown stats.DriveSpeed 2) / a_sum
        
        if distance > 2.0 * accelDist then
            let cruiseDist = distance - 2.0 * accelDist
            let cruiseTime = cruiseDist / stats.DriveSpeed
            accelTime * 2.0 + cruiseTime
        else
            let a_avg = a_sum / 2.0
            2.0 * sqrt(distance / a_avg)

    type AtmoProfile = {
        Gravity: float // m/s^2
        AtmoHeight: float // meters
        AtmoEfficiency: float // multiplier for main thrusters
    }

    let getAtmoProfile (name: string) (typeStr: string) =
        let n = name.ToLowerInvariant()
        if n.Contains("crusader") then { Gravity = 9.81; AtmoHeight = 90_000.0; AtmoEfficiency = 0.3 }
        elif typeStr.Equals("Planet", StringComparison.OrdinalIgnoreCase) || n.Contains("microtech") || n.Contains("hurston") || n.Contains("arccorp") then 
            { Gravity = 9.81; AtmoHeight = 12_000.0; AtmoEfficiency = 0.5 }
        elif typeStr.Equals("Moon", StringComparison.OrdinalIgnoreCase) then 
            { Gravity = 3.4; AtmoHeight = 3_000.0; AtmoEfficiency = 0.9 }
        else { Gravity = 0.0; AtmoHeight = 0.0; AtmoEfficiency = 1.0 }

    let defaultShip = {
        Name = "Default"
        Mass = 242177.0 // Cutlass Black
        CargoCapacity = 46
        MaxSpeed = 1150.0
        MainThrust = 18830926.0
    }

    let calculateTakeoffTime (profile: AtmoProfile) (ship: ShipStats) =
        if profile.AtmoHeight <= 0.0 then 0.0
        else
            // a_up = (T / m) * eff - g
            let a_up = (ship.MainThrust / ship.Mass) * profile.AtmoEfficiency - profile.Gravity
            if a_up <= 0.0 then 9999.0 // Cannot takeoff
            else
                let t_accel = ship.MaxSpeed / a_up
                let d_accel = 0.5 * a_up * t_accel * t_accel
                if d_accel >= profile.AtmoHeight then
                    sqrt (2.0 * profile.AtmoHeight / a_up)
                else
                    let d_cruise = profile.AtmoHeight - d_accel
                    let t_cruise = d_cruise / ship.MaxSpeed
                    t_accel + t_cruise

    let calculateLandingTime (profile: AtmoProfile) (ship: ShipStats) =
        if profile.AtmoHeight <= 0.0 then 0.0
        else
            // a_down = (T / m) + g
            let a_down = (ship.MainThrust / ship.Mass) + profile.Gravity
            let t_accel = ship.MaxSpeed / a_down
            let d_accel = 0.5 * a_down * t_accel * t_accel
            
            // Braking via NAV -> SCM drop. Drops max speed to ~200 in ~5 seconds.
            if profile.AtmoHeight <= d_accel then
                sqrt (2.0 * profile.AtmoHeight / a_down) + 5.0
            else
                let d_cruise = profile.AtmoHeight - d_accel - 1000.0 // 1km for braking buffer
                let cruiseDist = max 0.0 d_cruise
                let t_cruise = cruiseDist / ship.MaxSpeed
                t_accel + t_cruise + 10.0 // 10 seconds for NAV drop + touchdown

    let estimateTravelTime (fromLoc: LocationInfo) (toLoc: LocationInfo) (locations: LocationIndex) (appState: AppState) : float =
        if fromLoc.Uuid = toLoc.Uuid then 0.0
        else
            let fromBody = LocationResolver.getParentBody fromLoc locations
            let toBody = LocationResolver.getParentBody toLoc locations
            let ship = appState.Ship |> Option.defaultValue defaultShip
            let qd = appState.QuantumDrive |> Option.defaultValue defaultHemeraDrive
            
            let atmoSource = defaultArg fromBody fromLoc
            let isFromSpaceStation = fromLoc.Type = "Station" || fromLoc.Type = "SpaceStation" || fromLoc.Name.Contains("Station") || fromLoc.Name.Contains("Port ") || fromLoc.Name.Contains(" Baijini") || fromLoc.Name.Contains("Everus") || fromLoc.Name.Contains("Seraphim")
            
            let atmoExit = 
                if isFromSpaceStation then 0.0
                else
                    let profile = getAtmoProfile atmoSource.Name atmoSource.Type
                    calculateTakeoffTime profile ship
            
            let atmoDest = defaultArg toBody toLoc
            let isToSpaceStation = toLoc.Type = "Station" || toLoc.Type = "SpaceStation" || toLoc.Name.Contains("Station") || toLoc.Name.Contains("Port ") || toLoc.Name.Contains(" Baijini") || toLoc.Name.Contains("Everus") || toLoc.Name.Contains("Seraphim")
            
            let atmoEnter =
                if isToSpaceStation then 0.0
                else
                    let profile = getAtmoProfile atmoDest.Name atmoDest.Type
                    calculateLandingTime profile ship

            let mutable qtTime = 0.0

            if fromBody = toBody && fromBody.IsSome then
                let body = fromBody.Value
                let dx = fromLoc.Position.X - toLoc.Position.X
                let dy = fromLoc.Position.Y - toLoc.Position.Y
                let dz = fromLoc.Position.Z - toLoc.Position.Z
                let distanceEuc = sqrt (dx*dx + dy*dy + dz*dz)

                if distanceEuc < 20_000.0 then 
                    qtTime <- distanceEuc / 200.0 // SameSurface (No QT, approx 200m/s)
                else 
                    // Spline Jump (SameBodyQT)
                    let rx1 = fromLoc.Position.X - body.Position.X
                    let ry1 = fromLoc.Position.Y - body.Position.Y
                    let rz1 = fromLoc.Position.Z - body.Position.Z
                    let rx2 = toLoc.Position.X - body.Position.X
                    let ry2 = toLoc.Position.Y - body.Position.Y
                    let rz2 = toLoc.Position.Z - body.Position.Z
                    
                    let mag1 = sqrt (rx1*rx1 + ry1*ry1 + rz1*rz1)
                    let mag2 = sqrt (rx2*rx2 + ry2*ry2 + rz2*rz2)
                    let dot = rx1*rx2 + ry1*ry2 + rz1*rz2
                    
                    let cosTheta = dot / (mag1 * mag2)
                    let cosThetaClamped = max -1.0 (min 1.0 cosTheta)
                    let theta = acos cosThetaClamped
                    
                    let radius = getPlanetRadius body.Name body.Type
                    let arcDistance = radius * theta
                    
                    qtTime <- calculateKinematicTime arcDistance qd.Spline + qd.Spline.SpoolUpTime
            elif fromBody.IsSome && toBody.IsSome && LocationResolver.sharePlanet fromBody.Value toBody.Value locations then
                // SamePlanetMoonsQT
                let dist = LocationResolver.euclideanDistance fromLoc.Position toLoc.Position
                qtTime <- calculateKinematicTime dist qd.Standard + qd.Standard.SpoolUpTime
            else
                // InterplanetaryQT
                let dist = 
                    match fromBody, toBody with
                    | Some fb, Some tb -> LocationResolver.euclideanDistance fb.Position tb.Position
                    | _ -> LocationResolver.euclideanDistance fromLoc.Position toLoc.Position
                qtTime <- calculateKinematicTime dist qd.Standard + qd.Standard.SpoolUpTime

            let totalTime = atmoExit + qtTime + atmoEnter
            totalTime


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
            | PickupPackage (m, o, _) -> m, o
            | DropoffPackage (m, o, _) -> m, o
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
                    | Courier, Pickup -> PickupPackage (m.MissionId, obj.ObjectiveId, obj.CargoType)
                    | Courier, Dropoff -> DropoffPackage (m.MissionId, obj.ObjectiveId, obj.CargoType)
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

    let private groupActionsToStops (nodes: RouteNode list) (startLocOpt: LocationInfo option) (locations: LocationIndex) (appState: AppState) : RouteStop list =
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
                    | Some curr -> estimateTravelTime curr loc locations appState
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
    let private solveBranchAndBound (nodes: RouteNode list) (startLoc: LocationInfo option) (capacity: int) (currentCargo: int) (locations: LocationIndex) (appState: AppState) =
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
                                | Some l -> estimateTravelTime l node.Location locations appState
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
    let private solveGreedy (nodes: RouteNode list) (startLoc: LocationInfo option) (capacity: int) (currentCargo: int) (locations: LocationIndex) (appState: AppState) =
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
                        | Some l -> estimateTravelTime l n.Location locations appState
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
                            let time = match lastLoc with | Some l -> estimateTravelTime l node.Location locations appState | None -> 0.0
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
            let optimizedNodes = 
                let shipCapacity = appState.Ship |> Option.map (fun s -> s.CargoCapacity) |> Option.defaultValue 0
                if nodes.Length <= 12 then
                    solveBranchAndBound nodes startLoc shipCapacity appState.CurrentCargoScu locations appState
                else
                    solveGreedy nodes startLoc shipCapacity appState.CurrentCargoScu locations appState
            
            let stops = groupActionsToStops optimizedNodes startLoc locations appState
            let totalTime = stops |> List.sumBy (fun s -> s.TravelTimeEstimate + s.ActionTimeEstimate)
            
            Some {
                Stops = stops
                TotalEstimatedTime = totalTime
                CurrentStopIndex = 0
            }
