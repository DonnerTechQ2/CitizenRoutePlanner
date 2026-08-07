namespace CitizenRoutePlanner.Core

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open System.Runtime.InteropServices

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

    let extractCargoTypeFromContractName (contractName: string) : string option =
        let parts = contractName.Split('_')
        if parts.Length >= 4 && contractName.StartsWith("HaulCargo_", StringComparison.OrdinalIgnoreCase) then
            // Format is usually HaulCargo_Topology_Category_Item_Location_Grade
            // e.g. HaulCargo_SingleToMulti3_Processed_Stims_Stanton4_SmallGrade1 -> Stims
            // If parts.[3] is "Mixed", use category parts.[2] (e.g. Waste)
            if String.Equals(parts.[3], "Mixed", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3 then
                Some parts.[2]
            else
                Some parts.[3]
        else
            None

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

    let parseEndMission (line: string) (ts: DateTimeOffset) =
        if line.Contains("<EndMission>") then
            let m = Regex.Match(line, @"<EndMission> Ending mission for player\. MissionId\[([a-fA-F0-9\-]+)\].*?CompletionType\[([^\]]+)\]")
            if m.Success then
                let missionId = Guid.Parse(m.Groups.[1].Value)
                match m.Groups.[2].Value.ToLowerInvariant() with
                | "complete" -> Some (ContractCompleted (ts, missionId, ""))
                | "fail" -> Some (ContractFailed (ts, missionId, ""))
                | "abandon" -> Some (ContractAbandoned (ts, missionId, ""))
                | _ -> None
            else None
        else None

    let parseObjectiveMarker (line: string) (ts: DateTimeOffset) =
        if line.Contains("Creating objective marker:") then
            let m = Regex.Match(line, @"Creating objective marker: missionId \[([0-9a-fA-F-]+)\], generator name \[([^\]]+)\], contract \[([^\]]+)\].*?contractDefinitionId\[([0-9a-fA-F-]+)\].*?objectiveId \[([a-zA-Z0-9-_]+)\].*?zoneHostId \[(\d+)\], position \[x: ([-\d.]+), y: ([-\d.]+), z: ([-\d.]+)\]")
            if m.Success then
                let parseF (s: string) = Double.Parse(s, Globalization.CultureInfo.InvariantCulture)
                let pos = { X = parseF m.Groups.[7].Value; Y = parseF m.Groups.[8].Value; Z = parseF m.Groups.[9].Value }
                let objIdStr = m.Groups.[5].Value
                let objType = 
                    if objIdStr.StartsWith("dropoff", StringComparison.OrdinalIgnoreCase) then Dropoff
                    elif objIdStr.StartsWith("pickup", StringComparison.OrdinalIgnoreCase) then Pickup
                    else Nav

                Some (ObjectiveMarkerCreated (
                    ts, 
                    Guid.Parse(m.Groups.[1].Value), 
                    m.Groups.[2].Value, 
                    m.Groups.[3].Value, 
                    Guid.Parse(m.Groups.[4].Value),
                    objIdStr,
                    objType,

                    uint64 m.Groups.[6].Value,
                    pos
                ))
            else None
        else None

    let parseNewObjective (line: string) (ts: DateTimeOffset) =
        if line.Contains("New Objective:") || line.Contains("Objective Complete:") || line.Contains("Objective Updated:") then
            let mIdMatch = Regex.Match(line, @"MissionId:\s*\[([0-9a-fA-F-]+)\]")
            if mIdMatch.Success then
                let mId = Guid.Parse(mIdMatch.Groups.[1].Value)
                
                let objIdMatch = Regex.Match(line, @"ObjectiveId:\s*\[([^\]]+)\]")
                let objId = if objIdMatch.Success && not (String.IsNullOrWhiteSpace(objIdMatch.Groups.[1].Value)) then Some objIdMatch.Groups.[1].Value else None

                let mCargo = Regex.Match(line, @"(?:New Objective:|Objective Complete:|Objective Updated:) Deliver (\d+)/(\d+) SCU of (.*?) [tT]o (.*?)(?:\s*:\s*)?""")
                if mCargo.Success then
                    Some (NewObjective (
                        ts, mId, objId, Some Dropoff,
                        Some (int mCargo.Groups.[1].Value),
                        Some (int mCargo.Groups.[2].Value),
                        Some (mCargo.Groups.[3].Value.Trim()),
                        Some (mCargo.Groups.[4].Value.Trim())
                    ))
                else
                    let mDeliver = Regex.Match(line, @"(?:New Objective:|Objective Complete:|Objective Updated:) Deliver (.*?) [tT]o (.*?)(?:\s*:\s*)?""")
                    if mDeliver.Success then
                        Some (NewObjective (
                            ts, mId, objId, Some Dropoff,
                            None, None,
                            Some (mDeliver.Groups.[1].Value.Trim()),
                            Some (mDeliver.Groups.[2].Value.Trim())
                        ))
                    else
                    let mCollectScu = Regex.Match(line, @"(?:New Objective:|Objective Complete:|Objective Updated:) Collect (\d+)(?:/(\d+))? SCU of (.*?) [fF]rom (.*?)(?:\s*:\s*)?""")
                    if mCollectScu.Success then
                        let curScu = int mCollectScu.Groups.[1].Value
                        let totScu = if mCollectScu.Groups.[2].Success then int mCollectScu.Groups.[2].Value else curScu
                        Some (NewObjective (
                            ts, mId, objId, Some Pickup,
                            Some curScu, Some totScu,
                            Some (mCollectScu.Groups.[3].Value.Trim()),
                            Some (mCollectScu.Groups.[4].Value.Trim())
                        ))
                    else
                        let mCollect = Regex.Match(line, @"(?:New Objective:|Objective Complete:|Objective Updated:) Collect (.*?) [fF]rom (.*?)(?:\s*:\s*)?""")
                        if mCollect.Success then
                            Some (NewObjective (
                                ts, mId, objId, Some Pickup,
                                None, None,
                                Some (mCollect.Groups.[1].Value.Trim()),
                                Some (mCollect.Groups.[2].Value.Trim())
                            ))
                        else
                            let mGoto = Regex.Match(line, @"(?:New Objective:|Objective Complete:|Objective Updated:) Go [tT]o (.*?)(?:\s*:\s*)?""")
                            if mGoto.Success then
                                Some (NewObjective (
                                    ts, mId, objId, Some Nav,
                                    None, None,
                                    None,
                                    Some (mGoto.Groups.[1].Value.Trim())
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
            parseEndMission
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
                            let mutable lastLine = ""
                            
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
                                        
                                        let mutable finalLine = line
                                        if line.Contains("> : \" [") && not (String.IsNullOrEmpty lastLine) && lastLine.Contains("Added notification \"") then
                                            let tsEnd = line.IndexOf("> ")
                                            if tsEnd > 0 then
                                                finalLine <- lastLine + line.Substring(tsEnd + 1)
                                        
                                        match parseLine finalLine with
                                        | Some ev -> onEvent ev
                                        | None -> ()
                                        
                                        lastLine <- line
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

    [<Literal>]
    let private PROCESS_QUERY_LIMITED_INFORMATION = 0x1000u

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern nativeint private OpenProcess(uint32 dwDesiredAccess, bool bInheritHandle, int dwProcessId)

    [<DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)>]
    extern bool private QueryFullProcessImageName(nativeint hProcess, int dwFlags, StringBuilder lpExeName, int& lpdwSize)

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool private CloseHandle(nativeint hObject)

    let private tryGetProcessImagePath (proc: Process) =
        let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id)
        if handle = nativeint 0 then None
        else
            try
                let buffer = StringBuilder(4096)
                let mutable size = buffer.Capacity
                if QueryFullProcessImageName(handle, 0, buffer, &size) && size > 0 then
                    Some(buffer.ToString(0, size))
                else None
            finally
                CloseHandle(handle) |> ignore

    let private candidateLogPaths (exePath: string) =
        let exeDir = Path.GetDirectoryName(exePath)
        if String.IsNullOrWhiteSpace(exeDir) then
            Seq.empty
        else
            seq {
                yield Path.Combine(exeDir, "Game.log")
                let parent = Directory.GetParent(exeDir)
                if not (isNull parent) then
                    yield Path.Combine(parent.FullName, "Game.log")
            }

    let findGameLogPath () =
        Process.GetProcessesByName("StarCitizen")
        |> Seq.tryPick (fun proc ->
            tryGetProcessImagePath proc
            |> Option.bind (fun exePath ->
                candidateLogPaths exePath
                |> Seq.tryFind File.Exists))
