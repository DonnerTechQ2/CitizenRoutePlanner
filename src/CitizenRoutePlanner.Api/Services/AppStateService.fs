namespace CitizenRoutePlanner.Api.Services

open System
open CitizenRoutePlanner.Core

type AppStateService() =
    let mutable state : AppState = {
        Missions = Map.empty
        CurrentRoute = None
        PlayerLocation = None
        QuantumDestination = None
        Ship = None
        CurrentCargoScu = 0
        QuantumDrive = None
    }

    let mutable connectionStatus = "Game.log not found"

    let shipChanged = new Event<ShipStats>()
    let quantumDriveChanged = new Event<QuantumDriveStats>()
    let debugLogLine = new Event<string>()

    member this.GetState() = state
    
    member this.SetState(newState: AppState) =
        state <- newState

    member this.GetConnectionStatus() = connectionStatus
    member this.SetConnectionStatus(status: string) = connectionStatus <- status

    member this.UpdateShip(ship: ShipStats) =
        shipChanged.Trigger(ship)

    member this.UpdateQuantumDrive(stats: QuantumDriveStats) =
        quantumDriveChanged.Trigger(stats)

    member this.ShipChanged = shipChanged.Publish
    member this.QuantumDriveChanged = quantumDriveChanged.Publish
    member this.DebugLogLine = debugLogLine.Publish
    
    member this.InjectLine(line: string) = 
        debugLogLine.Trigger(line)
