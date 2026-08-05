namespace CitizenRoutePlanner.Api.Services

open System
open CitizenRoutePlanner.Core

type AppStateService() =
    let mutable state : AppState = {
        Missions = Map.empty
        CurrentRoute = None
        PlayerLocation = None
        QuantumDestination = None
        ShipCapacityScu = 16
        CurrentCargoScu = 0
        ShipSpeedModifier = 1.0
    }

    let mutable connectionStatus = "Game.log not found"

    let capacityChanged = new Event<int>()
    let speedModifierChanged = new Event<float>()
    let debugLogLine = new Event<string>()

    member this.GetState() = state
    
    member this.SetState(newState: AppState) =
        state <- newState

    member this.GetConnectionStatus() = connectionStatus
    member this.SetConnectionStatus(status: string) = connectionStatus <- status

    member this.UpdateCapacity(scu: int) =
        capacityChanged.Trigger(scu)

    member this.UpdateSpeedModifier(modf: float) =
        speedModifierChanged.Trigger(modf)

    member this.CapacityChanged = capacityChanged.Publish
    member this.SpeedModifierChanged = speedModifierChanged.Publish
    member this.DebugLogLine = debugLogLine.Publish
    
    member this.InjectLine(line: string) = 
        debugLogLine.Trigger(line)
