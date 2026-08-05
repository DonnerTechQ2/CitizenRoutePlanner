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

    /// Загружает locations-positions.json и строит все индексы.
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

        let all =
            arr
            |> Seq.cast<JsonNode>
            |> Seq.map (fun node ->
                {
                    Uuid       = match parseGuidOpt node.["uuid"] with | Some g -> g | None -> Guid.Empty
                    Name       = node.["name"].GetValue<string>()
                    Type       = node.["type"].GetValue<string>()
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
            |> List.map (fun l -> l.Name.ToLowerInvariant(), l)
            |> Map.ofList

        let planets  = all |> List.filter (fun l -> l.Type = "Planet")
        let moons    = all |> List.filter (fun l -> l.Type = "Moon")

        {
            All             = all
            ByUuid          = byUuid
            ByName          = byName
            CelestialBodies = planets @ moons
            Planets         = planets
            Moons           = moons
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
                        if parent.Type = "Planet" || parent.Type = "Moon" then Some parent
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

    /// Максимальная погрешность матча при координатной математике (50 км).
    [<Literal>]
    let MaxInferenceDistanceM = 50_000.0

    // ──────────────────────────────────────────────────────────────────────────
    // Основной алгоритм резолва
    // ──────────────────────────────────────────────────────────────────────────

    /// Гибридный алгоритм определения локации.
    ///
    /// Шаг 1 (Приоритет 1) — прямой матч по имени из JSON.
    /// Шаг 2 (Приоритет 2) — математика координат (только если |coords| > 10 км).
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
                Map.tryFind (name.ToLowerInvariant()) index.ByName
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
                    // Перебираем все небесные тела, для каждого:
                    // absPos = body.Position + marker.Position
                    // и ищем ближайшую реальную локацию
                    index.CelestialBodies
                    |> List.choose (fun body ->
                        let absPos = addCoords body.Position marker.Position
                        match findNearestLocation absPos index.All with
                        | None                 -> None
                        | Some (nearest, dist) ->
                            if dist <= MaxInferenceDistanceM then
                                Some (nearest, absPos, dist)
                            else None
                    )
                    // Берём вариант с наименьшей погрешностью
                    |> List.sortBy (fun (_, _, d) -> d)
                    |> List.tryHead
            )

        match coordResult with
        | Some (loc, absPos, dist) -> InferredLocation (loc, absPos, dist)
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
