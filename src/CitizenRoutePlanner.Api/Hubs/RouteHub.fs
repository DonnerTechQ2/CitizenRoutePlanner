namespace CitizenRoutePlanner.Api.Hubs

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR
open CitizenRoutePlanner.Api.Services
open CitizenRoutePlanner.Core

type RouteHub(appStateService: AppStateService) =
    inherit Hub()

    override this.OnConnectedAsync() =
        let caller = this.Clients.Caller
        let baseTask = base.OnConnectedAsync()
        task {
            do! baseTask
            let state = appStateService.GetState()
            
            for KeyValue(_, m) in state.Missions do
                do! caller.SendAsync("MissionAdded", m)
            
            if state.CurrentRoute.IsSome then
                do! caller.SendAsync("RouteUpdated", state.CurrentRoute)
            
            if state.PlayerLocation.IsSome then
                do! caller.SendAsync("PlayerLocationUpdated", state.PlayerLocation)
            
            do! caller.SendAsync("ConnectionStatus", appStateService.GetConnectionStatus())
        } :> Task

    member this.SetShip(ship: ShipStats) =
        appStateService.UpdateShip(ship)

    member this.SetQuantumDrive(stats: QuantumDriveStats) =
        appStateService.UpdateQuantumDrive(stats)
