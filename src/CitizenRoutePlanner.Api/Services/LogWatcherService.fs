namespace CitizenRoutePlanner.Api.Services

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.SignalR
open CitizenRoutePlanner.Core
open CitizenRoutePlanner.Api.Hubs

type WatcherMessage =
    | LogEvent of LogParser.LogEvent
    | ShipChanged of ShipStats
    | QuantumDriveChanged of QuantumDriveStats
    | DebugLine of string

type LogWatcherService(
    logger: ILogger<LogWatcherService>,
    hubContext: IHubContext<RouteHub>,
    appStateService: AppStateService) =
    inherit BackgroundService()

    let mutable logTailer : LogParser.LogTailer option = None
    let mutable locationIndex : LocationIndex option = None

    let broadcastState (oldState: AppState) (newState: AppState) =
        task {
            // Missions diff
            for KeyValue(id, newM) in newState.Missions do
                match oldState.Missions.TryFind id with
                | None -> do! hubContext.Clients.All.SendAsync("MissionAdded", newM)
                | Some oldM when oldM <> newM -> do! hubContext.Clients.All.SendAsync("MissionUpdated", newM)
                | _ -> ()

            for KeyValue(id, _) in oldState.Missions do
                if not (newState.Missions.ContainsKey id) then
                    do! hubContext.Clients.All.SendAsync("MissionRemoved", id)

            // Route diff
            if oldState.CurrentRoute <> newState.CurrentRoute then
                do! hubContext.Clients.All.SendAsync("RouteUpdated", newState.CurrentRoute)

            // Player location diff
            if oldState.PlayerLocation <> newState.PlayerLocation then
                do! hubContext.Clients.All.SendAsync("PlayerLocationUpdated", newState.PlayerLocation)
        } |> ignore


    let agent = MailboxProcessor<WatcherMessage>.Start(fun inbox ->
        let rec loop (pendingRecalc: bool) (baseState: AppState option) = async {
            let timeout = if pendingRecalc then 10 else -1
            let! msgOpt = inbox.TryReceive(timeout)
            try
                match msgOpt with
                | Some msg ->
                    match locationIndex with
                    | Some idx ->
                        let currentState = appStateService.GetState()
                        let originalBaseState = defaultArg baseState currentState
                        let mutable stateChanged = false
                        
                        let stateAfterEvent = 
                            match msg with
                            | LogEvent ev -> 
                                stateChanged <- true
                                MissionManager.processEvent idx currentState ev
                            | ShipChanged ship ->
                                stateChanged <- true
                                { currentState with Ship = Some ship }
                            | QuantumDriveChanged stats ->
                                stateChanged <- true
                                { currentState with QuantumDrive = Some stats }
                            | DebugLine line ->
                                appStateService.SetConnectionStatus("Simulating...")
                                hubContext.Clients.All.SendAsync("ConnectionStatus", {| logPath = "Simulating..." |}) |> ignore
                                match LogParser.parseLine line with
                                | Some ev -> 
                                    stateChanged <- true
                                    MissionManager.processEvent idx currentState ev
                                | None -> currentState

                        if stateChanged then
                            appStateService.SetState(stateAfterEvent)
                            return! loop true (Some originalBaseState)
                        else
                            return! loop pendingRecalc baseState
                    | None ->
                        logger.LogWarning("Event dropped because LocationIndex is not loaded.")
                        return! loop pendingRecalc baseState
                | None ->
                    if pendingRecalc then
                        match locationIndex with
                        | Some idx ->
                            let stateBeforeRecalc = appStateService.GetState()
                            let routeOpt = RouteEngine.calculateRoute stateBeforeRecalc idx
                            let finalState = { stateBeforeRecalc with CurrentRoute = routeOpt }
                            appStateService.SetState(finalState)
                            
                            let oldState = defaultArg baseState finalState
                            broadcastState oldState finalState
                        | None -> ()
                    return! loop false None
            with ex ->
                logger.LogError(ex, "Error processing event in agent")
                return! loop pendingRecalc baseState
        }
        loop false None
    )

    override this.ExecuteAsync(stoppingToken: CancellationToken) =
        task {
            logger.LogInformation("LogWatcherService starting...")
            
            // Load LocationIndex for all available systems (Stanton, Pyro, Nyx)
            let searchDirs = [
                AppDomain.CurrentDomain.BaseDirectory
                Directory.GetCurrentDirectory()
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..")
            ]

            let index, loadedFiles = LocationResolver.findAndLoadIndices searchDirs

            if not (List.isEmpty loadedFiles) then
                locationIndex <- Some index
                let filesListStr = String.Join(", ", loadedFiles)
                logger.LogInformation($"LocationIndex loaded ({index.All.Length} locations) from files: {filesListStr}")
            else
                let searchedList = String.Join(", ", searchDirs)
                logger.LogWarning($"Could not find location JSON files in candidate directories: {searchedList}")

            appStateService.ShipChanged.Add(fun s -> agent.Post(ShipChanged s))
            appStateService.QuantumDriveChanged.Add(fun stats -> agent.Post(QuantumDriveChanged stats))
            appStateService.DebugLogLine.Add(fun line -> agent.Post(DebugLine line))

            // Polling loop for Game.log
            while not stoppingToken.IsCancellationRequested do
                match LogParser.findGameLogPath() with
                | Some path when File.Exists(path) ->
                    logger.LogInformation($"Found Game.log at: {path}")
                    appStateService.SetConnectionStatus("Connected")
                    do! hubContext.Clients.All.SendAsync("ConnectionStatus", {| logPath = "Connected" |})
                    
                    let tailer = new LogParser.LogTailer(path)
                    logTailer <- Some tailer
                    tailer.Start(fun ev -> agent.Post(LogEvent ev))
                    
                    try
                        while not stoppingToken.IsCancellationRequested && File.Exists(path) do
                            do! Task.Delay(2000, stoppingToken)
                    with :? TaskCanceledException -> ()
                        
                    logger.LogWarning("Game.log no longer exists or service stopping. Stopping tailer...")
                    tailer.Stop()
                    logTailer <- None
                    appStateService.SetConnectionStatus("Game.log not found")
                    do! hubContext.Clients.All.SendAsync("ConnectionStatus", {| logPath = "Waiting for Game.log" |})
                | _ ->
                    logger.LogWarning("Game.log not found. Waiting for game to start...")
                    appStateService.SetConnectionStatus("Game.log not found")
                    do! hubContext.Clients.All.SendAsync("ConnectionStatus", {| logPath = "Waiting for Game.log" |})
                    try
                        do! Task.Delay(5000, stoppingToken)
                    with :? TaskCanceledException -> ()

            logger.LogInformation("LogWatcherService stopping...")
        } :> Task
