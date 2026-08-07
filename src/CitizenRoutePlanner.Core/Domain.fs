namespace CitizenRoutePlanner.Core

open System

type Coordinates = { X: float; Y: float; Z: float }

type QuantumModeStats = {
    DriveSpeed: float
    StageOneAccel: float
    StageTwoAccel: float
    SpoolUpTime: float
    CooldownTime: float
}

type QuantumDriveStats = {
    Name: string
    Standard: QuantumModeStats
    Spline: QuantumModeStats
}


type LocationInfo = {
    Uuid: Guid
    Name: string
    Type: string                 // Outpost, Planet, Moon, LandingZone, ...
    System: string
    ParentUuid: Guid option
    QtValid: bool
    Position: Coordinates        // Абсолютные координаты
}

type ObjectiveType =
    | Pickup
    | Dropoff
    | Nav

type ObjectiveStatus = Pending | InProgress | Completed

type MissionObjective = {
    ObjectiveId: string          // "pickup_uuid_0", "dropoff_uuid_1"
    Type: ObjectiveType
    RawPosition: Coordinates     // Как пришло из лога (может быть локальное)
    ZoneHostId: uint64
    AbsolutePosition: Coordinates option  // После резолва
    ResolvedLocation: LocationInfo option // После матча с JSON
    ScuAmount: int option        // Сколько SCU (для грузовых)
    CargoType: string option
    DestinationName: string option
    Status: ObjectiveStatus
    PairedObjectiveId: string option
}

type MissionType = 
    | Courier            // FTL_Courier, RedWind_RecoverItem — пакет, без SCU
    | DirectHaul         // AToB — 1→1, с SCU
    | MultiHaul of stops: int  // SingleToMulti2/4 — 1→N, с SCU

type MissionScope = 
    | Local of bodyIndex: int   // _Stanton1_ .. _Stanton4_
    | System                     // _Stanton_

type MissionStatus = Active | Completed | Failed | Abandoned

type Mission = {
    MissionId: Guid
    Title: string
    GeneratorName: string
    ContractName: string
    ContractDefinitionId: Guid
    MissionType: MissionType
    Scope: MissionScope
    Objectives: MissionObjective list
    PendingObjectivesData: (ObjectiveType option * int option * string option * string option) list
    Status: MissionStatus
    AcceptedAt: DateTimeOffset
}

/// Что делать на остановке маршрута
type RouteAction = 
    | PickupCargo of missionId: Guid * objectiveId: string * scuAmount: int option * cargoType: string option
    | DropoffCargo of missionId: Guid * objectiveId: string * scuAmount: int option * cargoType: string option
    | PickupPackage of missionId: Guid * objectiveId: string * cargoType: string option
    | DropoffPackage of missionId: Guid * objectiveId: string * cargoType: string option
    | NavTo of missionId: Guid * objectiveId: string

type RouteStop = {
    Location: LocationInfo
    Actions: RouteAction list
    TravelTimeEstimate: float    // Примерное время перелёта от предыдущей точки (секунды)
    ActionTimeEstimate: float    // Примерное время на выполнение действий в точке (секунды)
}

type Route = {
    Stops: RouteStop list
    TotalEstimatedTime: float    // Общее время маршрута (секунды)
    CurrentStopIndex: int
}

type ShipStats = {
    Name: string
    Mass: float
    CargoCapacity: int
    MaxSpeed: float
    MainThrust: float
}

type AppState = {
    Missions: Map<Guid, Mission>
    CurrentRoute: Route option
    PlayerLocation: LocationInfo option
    QuantumDestination: LocationInfo option
    Ship: ShipStats option
    CurrentCargoScu: int
    QuantumDrive: QuantumDriveStats option
}
