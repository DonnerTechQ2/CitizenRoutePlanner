namespace CitizenRoutePlanner.Core

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Net.Http
open System.Collections.Concurrent
open System.Threading

// ─────────────────────────────────────────────────────────────────────────────
// Типы
// ─────────────────────────────────────────────────────────────────────────────

/// Информация о маркере из лога (локальные или планетарные координаты)
type MarkerInfo = {
    Position   : Coordinates
    ZoneHostId : uint64
}

/// Результат определения локации
type ResolvedLocation =
    /// Точный матч по имени из JSON
    | KnownLocation   of location: LocationInfo * absolutePosition: Coordinates
    /// Определено математикой координат (погрешность в метрах)
    | InferredLocation of location: LocationInfo * absolutePosition: Coordinates * distanceM: float
    /// Невозможно определить (интерьер здания или неизвестное место)
    | UnknownLocation  of nameHint: string option * zoneHostId: uint64

/// Индексы для быстрого поиска по загруженным локациям
type LocationIndex = {
    All              : LocationInfo list
    ByUuid           : Map<Guid, LocationInfo>
    /// Ключ — name.ToLowerInvariant() (фильтрует UNINITIALIZED)
    ByName           : Map<string, LocationInfo>
    /// Только Planet и Moon — для математики координат
    CelestialBodies  : LocationInfo list
    Planets          : LocationInfo list
    Moons            : LocationInfo list
    /// Небесные тела + точки Лагранжа (L1..L5) — для гибридного расчета координат
    ReferenceOrigins : Coordinates list
}

/// Обогащённые данные из Star Citizen Wiki API
type WikiLocationInfo = {
    Description : string option
    ParentName  : string option
    TypeName    : string option
    Amenities   : string list
    ImageUrl    : string option
}

// ─────────────────────────────────────────────────────────────────────────────
module LocationResolver =

    // ──────────────────────────────────────────────────────────────────────────
    // Загрузка данных
    // ──────────────────────────────────────────────────────────────────────────

    let private isUninitializedName (name: string) =
        name.Contains("<= UNINITIALIZED =>")

    let private normalizeName (name: string) =
        let n = name.ToLowerInvariant().Trim()
        match n with
        | "teasa spaceport" -> "lorville"
        | "riker memorial spaceport" -> "area18"
        | "august dunlow spaceport" -> "orison"
        | "new babbage interstellar spaceport" -> "new babbage"
        | _ ->
            // Convert "mic-l2" to "mic l2", but preserve things like "mic-l2 long forest station"
            let m = System.Text.RegularExpressions.Regex.Match(n, @"^(mic|arc|hur|cru)-(l\d(?:-[a-z])?)$")
            if m.Success then
                m.Groups.[1].Value + " " + m.Groups.[2].Value
            else
                n

    /// Генерирует координаты точек Лагранжа (L1..L5) для планеты относительно ее звезды.
    let getLagrangeOrigins (planet: LocationInfo) : Coordinates list =
        let px = planet.Position.X
        let py = planet.Position.Y
        let pz = planet.Position.Z
        
        let l1 = { X = 0.90001217 * px; Y = 0.90001217 * py; Z = pz }
        let l2 = { X = 1.10001217 * px; Y = 1.10001217 * py; Z = pz }
        let l3 = { X = -1.0 * px; Y = -1.0 * py; Z = pz }
        
        let cos60 = 0.5
        let sin60 = 0.8660254037844386
        let l4 = { X = px * cos60 - py * sin60; Y = px * sin60 + py * cos60; Z = pz }
        let l5 = { X = px * cos60 + py * sin60; Y = -px * sin60 + py * cos60; Z = pz }
        
        [l1; l2; l3; l4; l5]

    /// Загружает locations-positions.json и строит все индексы.
    /// Данные запрашиваются с API: https://api.star-citizen.wiki/api/locations/positions?filter[system]=stanton
    /// Принимает путь к файлу — чтобы тесты могли подставить реальный путь.
    let loadIndex (jsonPath: string) : LocationIndex =
        let json   = File.ReadAllText(jsonPath)
        let doc    = JsonNode.Parse(json)
        let arr    = doc.["data"].AsArray()

        let parseCoord (v: JsonNode) =
            match v with
            | null -> 0.0
            | n    -> n.GetValue<double>()

        let parseGuidOpt (v: JsonNode) =
            match v with
            | null -> None
            | n ->
                let s = n.GetValue<string>()
                match Guid.TryParse(s) with
                | true, g -> Some g
                | _       -> None

        let normalizeLocationType (t: string) =
            match t with
            | "Manmade_VisibleOnInteraction"
            | "Manmade" -> "SpaceStation"
            | other -> other

        let all =
            arr
            |> Seq.cast<JsonNode>
            |> Seq.map (fun node ->
                {
                    Uuid       = match parseGuidOpt node.["uuid"] with | Some g -> g | None -> Guid.Empty
                    Name       = node.["name"].GetValue<string>()
                    Type       = normalizeLocationType (node.["type"].GetValue<string>())
                    System     = node.["system"].GetValue<string>()
                    ParentUuid = parseGuidOpt node.["parent_uuid"]
                    QtValid    = node.["qt_valid"].GetValue<bool>()
                    Position   = {
                        X = parseCoord node.["x"]
                        Y = parseCoord node.["y"]
                        Z = parseCoord node.["z"]
                    }
                })
            |> Seq.toList

        let byUuid =
            all
            |> List.map (fun l -> l.Uuid, l)
            |> Map.ofList

        let byName =
            all
            |> List.filter (fun l -> not (isUninitializedName l.Name))
            |> List.map (fun l -> normalizeName l.Name, l)
            |> Map.ofList

        let planets  = all |> List.filter (fun l -> l.Type = "Planet")
        let moons    = all |> List.filter (fun l -> l.Type = "Moon")
        let stars    = all |> List.filter (fun l -> l.Type = "Star")

        let celestialBodies = stars @ planets @ moons
        let lagrangeOrigins = planets |> List.collect getLagrangeOrigins
        let referenceOrigins = (celestialBodies |> List.map (fun c -> c.Position)) @ lagrangeOrigins

        {
            All              = all
            ByUuid           = byUuid
            ByName           = byName
            CelestialBodies  = celestialBodies
            Planets          = planets
            Moons            = moons
            ReferenceOrigins = referenceOrigins
        }

    // ──────────────────────────────────────────────────────────────────────────
    // Математические утилиты
    // ──────────────────────────────────────────────────────────────────────────

    let euclideanDistance (a: Coordinates) (b: Coordinates) : float =
        let dx = a.X - b.X
        let dy = a.Y - b.Y
        let dz = a.Z - b.Z
        Math.Sqrt(dx*dx + dy*dy + dz*dz)

    let addCoords (a: Coordinates) (b: Coordinates) : Coordinates =
        { X = a.X + b.X; Y = a.Y + b.Y; Z = a.Z + b.Z }

    let maxAbsCoord (c: Coordinates) =
        Math.Max(Math.Abs c.X, Math.Max(Math.Abs c.Y, Math.Abs c.Z))

    /// Ищет ближайшую локацию из списка к заданной абсолютной позиции.
    /// Возвращает пару (локация, расстояние_в_метрах) или None если список пуст.
    let findNearestLocation (position: Coordinates) (locations: LocationInfo list) =
        match locations with
        | [] -> None
        | _  ->
            locations
            |> List.map (fun l -> l, euclideanDistance position l.Position)
            |> List.minBy snd
            |> Some

    /// Возвращает ближайшее «тело» (Planet или Moon) в иерархии.
    /// Для Moon возвращает саму луну.
    /// Для Planet возвращает саму планету.
    /// Для Outpost, LandingZone и т.д. — ищет через parent_uuid.
    let getParentBody (location: LocationInfo) (index: LocationIndex) : LocationInfo option =
        let celestialTypes = set ["Planet"; "Moon"; "Star"]
        if celestialTypes.Contains location.Type then
            Some location
        else
            let rec climb (uuidOpt: Guid option) =
                match uuidOpt with
                | None -> None
                | Some uuid ->
                    match Map.tryFind uuid index.ByUuid with
                    | None -> None
                    | Some parent ->
                        if parent.Type = "Planet" || parent.Type = "Moon" || parent.Type = "Star" then Some parent
                        else climb parent.ParentUuid
            climb location.ParentUuid

    /// Возвращает планету верхнего уровня (Planet) для данной локации.
    /// Для Moon: ищет её planet-parent.
    /// Для Planet: возвращает саму планету.
    let private getTopPlanet (location: LocationInfo) (index: LocationIndex) : LocationInfo option =
        let rec climb (loc: LocationInfo) =
            if loc.Type = "Planet" then Some loc
            elif loc.Type = "Star" || loc.ParentUuid.IsNone then None
            else
                match Map.tryFind loc.ParentUuid.Value index.ByUuid with
                | None        -> None
                | Some parent -> climb parent
        climb location

    /// Проверяет, что два тела (Planet или Moon) принадлежат одной планетной системе.
    /// Луна и её планета → true.
    /// Две луны одной планеты → true.
    /// Луны разных планет → false.
    let sharePlanet (bodyA: LocationInfo) (bodyB: LocationInfo) (index: LocationIndex) : bool =
        match getTopPlanet bodyA index, getTopPlanet bodyB index with
        | Some pA, Some pB -> pA.Uuid = pB.Uuid
        | _                -> false

    // ──────────────────────────────────────────────────────────────────────────
    // Пороги
    // ──────────────────────────────────────────────────────────────────────────

    /// Если max(|x|, |y|, |z|) маркера превышает этот порог —
    /// координаты считаются планетарными (в системе отсчёта небесного тела).
    /// 10 000 м = 10 км. Интерьеры/площадки значительно меньше этого.
    [<Literal>]
    let CoordScaleThreshold = 10_000.0

    /// Максимальная погрешность матча при координатной математике (150 км для космоса и точек Лагранжа).
    [<Literal>]
    let MaxInferenceDistanceM = 150_000.0

    // ──────────────────────────────────────────────────────────────────────────
    // Основной алгоритм резолва
    // ──────────────────────────────────────────────────────────────────────────

    /// Гибридный алгоритм определения локации.
    ///
    /// Шаг 1 (Приоритет 1) — прямой матч по имени из JSON.
    /// Шаг 2 (Приоритет 2) — математика координат (только если |coords| > 10 км).
    /// Шаг 2.5 — матч по Z-координате (fallback).
    /// Шаг 3 — Fallback → UnknownLocation.
    let resolveLocation
            (index    : LocationIndex)
            (nameOpt  : string option)
            (markerOpt: MarkerInfo option)
            : ResolvedLocation =

        // ── Шаг 1: матч по имени ──────────────────────────────────────────────
        let nameMatch =
            nameOpt
            |> Option.bind (fun name ->
                Map.tryFind (normalizeName name) index.ByName
            )

        match nameMatch with
        | Some loc -> KnownLocation (loc, loc.Position)
        | None ->

        // ── Шаг 2: математика координат ──────────────────────────────────────
        let coordResult =
            markerOpt |> Option.bind (fun marker ->
                if maxAbsCoord marker.Position < CoordScaleThreshold then
                    // Интерьер здания — вычислить абсолютные координаты невозможно
                    None
                else
                    // Фильтруем локации для поиска — только открытые (QtValid) и инициализированные
                    let validLocations =
                        index.All
                        |> List.filter (fun l -> l.QtValid && not (isUninitializedName l.Name))

                    // 1. Перебираем все опорные точки (небесные тела + точки Лагранжа)
                    let candidatesFromOrigins =
                        index.ReferenceOrigins
                        |> List.choose (fun originPos ->
                            let absPos = addCoords originPos marker.Position
                            match findNearestLocation absPos validLocations with
                            | None                 -> None
                            | Some (nearest, dist) ->
                                if dist <= MaxInferenceDistanceM then
                                    Some (nearest, absPos, dist)
                                else None
                        )

                    // 2. Для космических станций проверка совпадения Z-координаты (прямого или с астероидным смещением 8076.63 м, погрешность ≤ 0.5 м)
                    let candidateFromStationZ =
                        validLocations
                        |> List.choose (fun loc ->
                            let isStation = loc.Position.Z <> 0.0 && (loc.Type = "SpaceStation" || loc.Type = "Space Station" || loc.Type = "Manmade_VisibleOnInteraction" || loc.Type = "Manmade" || loc.Name.Contains("Station"))
                            if isStation then
                                let zDiffDirect = Math.Abs(loc.Position.Z - marker.Position.Z)
                                let zDiffOffset = Math.Abs(loc.Position.Z - (marker.Position.Z + 8076.63))
                                let zDiff = Math.Min(zDiffDirect, zDiffOffset)
                                if zDiff <= 0.5 then
                                    Some (loc, loc.Position, zDiff)
                                else None
                            else None
                        )

                    (candidatesFromOrigins @ candidateFromStationZ)
                    |> List.sortBy (fun (_, _, d) -> d)
                    |> List.tryHead
            )

        match coordResult with
        | Some (loc, absPos, dist) -> InferredLocation (loc, absPos, dist)
        | None ->

        // ── Шаг 2.5: матч по Z-координате (fallback, погрешность ≤ 0.5 м) ──────
        let zMatch =
            markerOpt |> Option.bind (fun marker ->
                let z = marker.Position.Z
                let matches =
                    index.All
                    |> List.filter (fun loc ->
                        loc.QtValid &&
                        not (isUninitializedName loc.Name) &&
                        loc.Position.Z <> 0.0 &&
                        (Math.Abs(loc.Position.Z - z) <= 0.5 || Math.Abs(loc.Position.Z - (z + 8076.63)) <= 0.5)
                    )
                match matches with
                | [] -> None
                | multiple ->
                    multiple
                    |> List.sortBy (fun loc ->
                        let isStation = loc.Type = "SpaceStation" || loc.Type = "Space Station" || loc.Type = "Manmade_VisibleOnInteraction" || loc.Type = "Manmade" || loc.Name.Contains("Station")
                        let isClinic = loc.Name.Contains("Clinic")
                        let zDiff = Math.Min(Math.Abs(loc.Position.Z - z), Math.Abs(loc.Position.Z - (z + 8076.63)))
                        (if isClinic then 2 else if isStation then 0 else 1), zDiff
                    )
                    |> List.tryHead
            )

        match zMatch with
        | Some loc -> KnownLocation (loc, loc.Position)
        | None ->

        // ── Шаг 3: Fallback ───────────────────────────────────────────────────
        let zoneHostId =
            markerOpt |> Option.map (fun m -> m.ZoneHostId) |> Option.defaultValue 0UL
        UnknownLocation (nameOpt, zoneHostId)

    // ──────────────────────────────────────────────────────────────────────────
    // Wiki API клиент (опциональное обогащение)
    // ──────────────────────────────────────────────────────────────────────────

    /// Глобальный HTTP клиент (thread-safe, должен быть singleton)
    let private httpClient =
        let c = new HttpClient()
        c.DefaultRequestHeaders.Add("Accept", "application/json")
        c.Timeout <- TimeSpan.FromSeconds(5.0)
        c

    /// Кеш uuid → WikiLocationInfo option
    let private wikiCache = ConcurrentDictionary<Guid, WikiLocationInfo option>()

    /// Семафор для rate limiting (≤ 2 req/sec)
    let private rateLimiter = new SemaphoreSlim(1, 1)

    let private parseWikiResponse (json: string) : WikiLocationInfo option =
        try
            let doc = JsonNode.Parse(json)
            let data = doc.["data"]
            if data = null then None
            else
                let desc =
                    match data.["description"] with
                    | null -> None
                    | n    -> Some (n.GetValue<string>())

                let parentName =
                    match data.["parent"] with
                    | null -> None
                    | p    ->
                        match p.["name"] with
                        | null -> None
                        | n    -> Some (n.GetValue<string>())

                let typeName =
                    match data.["type"] with
                    | null -> None
                    | t    ->
                        match t.["name"] with
                        | null -> None
                        | n    -> Some (n.GetValue<string>())

                let amenities =
                    match data.["amenities"] with
                    | null -> []
                    | arr  ->
                        arr.AsArray()
                        |> Seq.cast<JsonNode>
                        |> Seq.choose (fun a ->
                            match a.["display_name"] with
                            | null -> None
                            | n    -> Some (n.GetValue<string>())
                        )
                        |> Seq.toList

                let imageUrl =
                    match data.["images"] with
                    | null -> None
                    | arr  ->
                        let images = arr.AsArray()
                        if images.Count = 0 then None
                        else
                            match images.[0].["thumbnail_url"] with
                            | null -> None
                            | n    -> Some (n.GetValue<string>())

                Some {
                    Description = desc
                    ParentName  = parentName
                    TypeName    = typeName
                    Amenities   = amenities
                    ImageUrl    = imageUrl
                }
        with _ -> None

    /// Загружает информацию о локации из Star Citizen Wiki API.
    /// Результат кешируется. При ошибке сети возвращает None (graceful degradation).
    let fetchWikiInfo (uuid: Guid) : Async<WikiLocationInfo option> =
        async {
            match wikiCache.TryGetValue(uuid) with
            | true, cached -> return cached
            | _            ->
                // Rate limit — не более 1 запроса в очереди + 500ms задержки
                do! Async.AwaitTask(rateLimiter.WaitAsync())
                try
                    // ПОВТОРНАЯ ПРОВЕРКА КЕША (Double-check locking)
                    match wikiCache.TryGetValue(uuid) with
                    | true, cached -> return cached
                    | _ ->
                        try
                            let url = $"https://api.star-citizen.wiki/api/locations/{uuid}"
                            let! response = httpClient.GetStringAsync(url) |> Async.AwaitTask
                            do! Async.Sleep(500) // ≤ 2 req/sec
                            let result = parseWikiResponse response
                            wikiCache.TryAdd(uuid, result) |> ignore
                            return result
                        with _ ->
                            wikiCache.TryAdd(uuid, None) |> ignore
                            return None
                finally
                    rateLimiter.Release() |> ignore
        }
