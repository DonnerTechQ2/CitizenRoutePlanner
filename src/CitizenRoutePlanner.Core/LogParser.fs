namespace CitizenRoutePlanner.Core

open System
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Diagnostics

module LogParser =

    type LogEvent =
        | ContractAccepted of timestamp: DateTimeOffset * missionId: Guid * title: string
        | ContractCompleted of timestamp: DateTimeOffset * missionId: Guid * title: string
        | ContractFailed of timestamp: DateTimeOffset * missionId: Guid * title: string
        | ContractAbandoned of timestamp: DateTimeOffset * missionId: Guid * title: string
        | ObjectiveMarkerCreated of timestamp: DateTimeOffset * missionId: Guid * generatorName: string * contractName: string * contractDefId: Guid * objectiveId: string * objectiveType: ObjectiveType * zoneHostId: uint64 * position: Coordinates
        | NewObjective of timestamp: DateTimeOffset * missionId: Guid * objectiveId: string option * targetObjectiveTypeHint: ObjectiveType option * scuCurrent: int option * scuTotal: int option * cargoType: string option * destinationName: string option
        | ObjectiveStateChanged of timestamp: DateTimeOffset * missionId: Guid * objectiveId: string * state: ObjectiveStatus
        | QuantumRouteCalculated of timestamp: DateTimeOffset * startLocation: string * destination: string
        | QuantumArrived of timestamp: DateTimeOffset
        | ItemRegistered of timestamp: DateTimeOffset * missionId: Guid * pickupObjectiveId: string * dropoffObjectiveId: string * itemName: string

    let determineMissionType (contractName: string) =
        let c = contractName.ToLowerInvariant()
        if c.Contains("multi4") then MultiHaul 4
        elif c.Contains("multi3") then MultiHaul 3
        elif c.Contains("multi2") then MultiHaul 2
        elif c.Contains("atob") 
             || c.Contains("redwind_intro") 
             || c.Contains("redwind_asd") 
             || c.Contains("redwind_pyro_bulkgrade") 
             || c.Contains("redwind_pyro_supplygrade") then
            DirectHaul
        else
            Courier

    let determineMissionScope (contractName: string) =
        let m = Regex.Match(contractName, @"_Stanton(\d)_")
        if m.Success then
            Local (int m.Groups.[1].Value)
        elif contractName.Contains("_Stanton_") then
            System
        else
            System

    let parseTimestamp (line: string) : DateTimeOffset =
        let m = Regex.Match(line, @"^<([^>]+)>")
        if m.Success then
            match DateTimeOffset.TryParse(m.Groups.[1].Value, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
            | true, dt -> dt
            | _ -> DateTimeOffset.UtcNow
        else
            DateTimeOffset.UtcNow

    let parseContractAccepted (line: string) (ts: DateTimeOffset) =
        if line.Contains("Contract Accepted:") then
            let m = Regex.Match(line, @"Contract Accepted:\s*(.*?)(?:\s*:\s*)?""\s*\[\d+\].*?MissionId:\s*\[([0-9a-fA-F-]+)\]")
            if m.Success then
                Some (ContractAccepted (ts, Guid.Parse(m.Groups.[2].Value), m.Groups.[1].Value.Trim()))
            else None
        else None

    let parseContractCompleted (line: string) (ts: DateTimeOffset) =
        if line.Contains("Contract Complete:") then
            let m = Regex.Match(line, @"Contract Complete:\s*(.*?)(?:\s*:\s*)?""\s*\[\d+\].*?MissionId:\s*\[([0-9a-fA-F-]+)\]")
            if m.Success then
                Some (ContractCompleted (ts, Guid.Parse(m.Groups.[2].Value), m.Groups.[1].Value.Trim()))
            else None
        else None

    let parseContractFailed (line: string) (ts: DateTimeOffset) =
        if line.Contains("Contract Failed:") then
            let m = Regex.Match(line, @"Contract Failed:\s*(.*?)(?:\s*:\s*)?""\s*\[\d+\].*?MissionId:\s*\[([0-9a-fA-F-]+)\]")
            if m.Success then
                Some (ContractFailed (ts, Guid.Parse(m.Groups.[2].Value), m.Groups.[1].Value.Trim()))
            else None
        else None

    let parseContractAbandoned (line: string) (ts: DateTimeOffset) =
        if line.Contains("Contract Abandoned:") || line.Contains("Contract Cancelled:") then
            let m = Regex.Match(line, @"Contract (?:Abandoned|Cancelled):\s*(.*?)(?:\s*:\s*)?""\s*\[\d+\].*?MissionId:\s*\[([0-9a-fA-F-]+)\]")
            if m.Success then
                Some (ContractAbandoned (ts, Guid.Parse(m.Groups.[2].Value), m.Groups.[1].Value.Trim()))
            else None
        else None

    let parseObjectiveMarker (line: string) (ts: DateTimeOffset) =
        if line.Contains("Creating objective marker:") then
            let m = Regex.Match(line, @"Creating objective marker: missionId \[([0-9a-fA-F-]+)\], generator name \[([^\]]+)\], contract \[([^\]]+)\].*?contractDefinitionId\[([0-9a-fA-F-]+)\].*?objectiveId \[(pickup|dropoff)_([a-zA-Z0-9-_]+)\].*?zoneHostId \[(\d+)\], position \[x: ([-\d.]+), y: ([-\d.]+), z: ([-\d.]+)\]")
            if m.Success then
                let objType = if m.Groups.[5].Value = "pickup" then Pickup else Dropoff
                let parseF (s: string) = Double.Parse(s, Globalization.CultureInfo.InvariantCulture)
                let pos = { X = parseF m.Groups.[8].Value; Y = parseF m.Groups.[9].Value; Z = parseF m.Groups.[10].Value }
                Some (ObjectiveMarkerCreated (
                    ts, 
                    Guid.Parse(m.Groups.[1].Value), 
                    m.Groups.[2].Value, 
                    m.Groups.[3].Value, 
                    Guid.Parse(m.Groups.[4].Value),
                    m.Groups.[5].Value + "_" + m.Groups.[6].Value,
                    objType,
                    uint64 m.Groups.[7].Value,
                    pos
                ))
            else None
        else None

    let parseNewObjective (line: string) (ts: DateTimeOffset) =
        if line.Contains("New Objective:") then
            let mIdMatch = Regex.Match(line, @"MissionId:\s*\[([0-9a-fA-F-]+)\]")
            if mIdMatch.Success then
                let mId = Guid.Parse(mIdMatch.Groups.[1].Value)
                
                let objIdMatch = Regex.Match(line, @"ObjectiveId:\s*\[([^\]]+)\]")
                let objId = if objIdMatch.Success && not (String.IsNullOrWhiteSpace(objIdMatch.Groups.[1].Value)) then Some objIdMatch.Groups.[1].Value else None

                let mCargo = Regex.Match(line, @"New Objective: Deliver (\d+)/(\d+) SCU of (.*?) to (.*?)(?:\s*:\s*)?""")
                if mCargo.Success then
                    Some (NewObjective (
                        ts, mId, objId, Some Dropoff,
                        Some (int mCargo.Groups.[1].Value),
                        Some (int mCargo.Groups.[2].Value),
                        Some (mCargo.Groups.[3].Value.Trim()),
                        Some (mCargo.Groups.[4].Value.Trim())
                    ))
                else
                    let mDeliver = Regex.Match(line, @"New Objective: Deliver (.*?) To (.*?)(?:\s*:\s*)?""")
                    if mDeliver.Success then
                        Some (NewObjective (
                            ts, mId, objId, Some Dropoff,
                            None, None,
                            Some (mDeliver.Groups.[1].Value.Trim()),
                            Some (mDeliver.Groups.[2].Value.Trim())
                        ))
                    else
                        let mCollect = Regex.Match(line, @"New Objective: Collect (.*?) From (.*?)(?:\s*:\s*)?""")
                        if mCollect.Success then
                            Some (NewObjective (
                                ts, mId, objId, Some Pickup,
                                None, None,
                                Some (mCollect.Groups.[1].Value.Trim()),
                                Some (mCollect.Groups.[2].Value.Trim())
                            ))
                        else None
            else None
        else None

    let parseObjectiveUpserted (line: string) (ts: DateTimeOffset) =
        if line.Contains("ObjectiveUpserted") then
            let m = Regex.Match(line, @"ObjectiveUpserted.*?mission_id ([0-9a-fA-F-]+) - objective_id (\S+) - state MISSION_OBJECTIVE_STATE_(\w+)")
            if m.Success then
                let status = 
                    match m.Groups.[3].Value with
                    | "COMPLETED" -> ObjectiveStatus.Completed
                    | "INPROGRESS" -> ObjectiveStatus.InProgress
                    | _ -> ObjectiveStatus.Pending
                Some (ObjectiveStateChanged (ts, Guid.Parse(m.Groups.[1].Value), m.Groups.[2].Value, status))
            else None
        else None
        
    let parseQuantumRouteCalculated (line: string) (ts: DateTimeOffset) =
        if line.Contains("CalculateRoute") then
            let m = Regex.Match(line, @"CalculateRoute\|Projected Start Location is (.*?) for route to destination (.*?)$")
            if m.Success then
                Some (QuantumRouteCalculated (ts, m.Groups.[1].Value.Trim(), m.Groups.[2].Value.Trim()))
            else None
        else None

    let parseQuantumArrived (line: string) (ts: DateTimeOffset) =
        if line.Contains("Quantum Drive has arrived at final destination") then
            Some (QuantumArrived ts)
        else None

    let parseItemRegistered (line: string) (ts: DateTimeOffset) =
        if line.Contains("Mission Item") then
            let m = Regex.Match(line, @"Mission Item (.*?) \(\d+\) registered with mission id ([0-9a-fA-F-]+), phase id [^,]*, pickup objective id ([^,]*), drop off objective id (\S*)")
            if m.Success then
                Some (ItemRegistered (ts, Guid.Parse(m.Groups.[2].Value), m.Groups.[3].Value, m.Groups.[4].Value, m.Groups.[1].Value.Trim()))
            else None
        else None

    let parseLine (line: string) : LogEvent option =
        let ts = parseTimestamp line
        
        let parsers = [
            parseContractAccepted
            parseContractCompleted
            parseContractFailed
            parseContractAbandoned
            parseObjectiveMarker
            parseNewObjective
            parseObjectiveUpserted
            parseQuantumRouteCalculated
            parseQuantumArrived
            parseItemRegistered
        ]
        
        parsers 
        |> Seq.tryPick (fun parser -> parser line ts)

    type LogTailer(filePath: string) =
        let mutable isRunning = false
        let mutable cts = new CancellationTokenSource()

        member this.Start(onEvent: LogEvent -> unit) =
            if isRunning then ()
            else
                isRunning <- true
                if cts.IsCancellationRequested then
                    cts <- new CancellationTokenSource()
                Task.Run(fun () ->
                    try
                        try
                            use fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
                            use reader = new StreamReader(fs)
                            
                            fs.Seek(0L, SeekOrigin.End) |> ignore
                            
                            let sb = System.Text.StringBuilder()
                            
                            while isRunning && not cts.IsCancellationRequested do
                                let ch = reader.Read()
                                if ch = -1 then
                                    if fs.Length < fs.Position then
                                        fs.Position <- 0L
                                        reader.DiscardBufferedData()
                                        sb.Clear() |> ignore
                                    Thread.Sleep(500)
                                else
                                    let c = char ch
                                    if c = '\n' then
                                        let line = sb.ToString().TrimEnd('\r')
                                        sb.Clear() |> ignore
                                        match parseLine line with
                                        | Some ev -> onEvent ev
                                        | None -> ()
                                    else
                                        sb.Append(c) |> ignore
                        with ex -> 
                            printfn "LogTailer error: %s" ex.Message
                    finally
                        isRunning <- false
                , cts.Token) |> ignore

        member this.Stop() =
            isRunning <- false
            cts.Cancel()

    let findGameLogPath () =
        try
            let procs = Process.GetProcessesByName("starcitizen")
            if procs.Length > 0 then
                let exePath = procs.[0].MainModule.FileName
                let dir1 = Path.GetDirectoryName(exePath)
                let log1 = Path.Combine(dir1, "Game.log")
                if File.Exists(log1) then Some log1
                else
                    let dir2 = Path.GetDirectoryName(dir1)
                    let log2 = Path.Combine(dir2, "Game.log")
                    if File.Exists(log2) then Some log2 else None
            else None
        with _ -> None
