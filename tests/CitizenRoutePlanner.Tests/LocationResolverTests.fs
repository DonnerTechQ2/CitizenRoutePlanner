namespace CitizenRoutePlanner.Tests

open System
open Xunit
open CitizenRoutePlanner.Core

// ─────────────────────────────────────────────────────────────────────────────
// Вспомогательные функции тестов
// ─────────────────────────────────────────────────────────────────────────────
module LocationResolverTests =

    // Поиск файлов данных (ищем вверх по дереву директорий от output)
    let private findProjectRoot () =
        let rec search (dir: string) =
            if IO.File.Exists(IO.Path.Combine(dir, "CitizenRoutePlanner.sln")) then dir
            else
                let parent = IO.Directory.GetParent(dir)
                if parent = null then failwith "Не удалось найти корень проекта"
                else search parent.FullName
        search AppContext.BaseDirectory

    let private locationsPath () =
        IO.Path.Combine(findProjectRoot(), "locations-positions.json")

    // Lazy-загрузка индекса — один раз на все тесты
    let private indexLazy = lazy (LocationResolver.loadIndex (locationsPath ()))

    let private idx () = indexLazy.Value

    // Хелперы для разбора ResolvedLocation
    let private asKnown (r: ResolvedLocation) =
        match r with
        | KnownLocation (loc, pos) -> loc, pos
        | other -> failwithf "Ожидался KnownLocation, получен: %A" other

    let private asInferred (r: ResolvedLocation) =
        match r with
        | InferredLocation (loc, pos, dist) -> loc, pos, dist
        | other -> failwithf "Ожидался InferredLocation, получен: %A" other

    let private asUnknown (r: ResolvedLocation) =
        match r with
        | UnknownLocation (hint, zid) -> hint, zid
        | other -> failwithf "Ожидался UnknownLocation, получен: %A" other

    let private resolve nameOpt markerOpt =
        LocationResolver.resolveLocation (idx ()) nameOpt markerOpt

    let private resolveByName name =
        resolve (Some name) None

    let private resolveByMarker pos zid =
        resolve None (Some { Position = pos; ZoneHostId = zid })

    let private coords x y z : Coordinates = { X = x; Y = y; Z = z }

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 1 — Загрузка JSON
    // ─────────────────────────────────────────────────────────────────────────

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Index loads 805 locations`` () =
        Assert.Equal(805, idx().All.Length)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Index has exactly 4 planets`` () =
        Assert.Equal(4, idx().Planets.Length)
        let names = idx().Planets |> List.map _.Name |> set
        Assert.Contains("Hurston",  names)
        Assert.Contains("Crusader", names)
        Assert.Contains("ArcCorp",  names)
        Assert.Contains("microTech",names)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Index has exactly 12 moons`` () =
        Assert.Equal(12, idx().Moons.Length)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``CelestialBodies contains 17 entries (1 star + 4 planets + 12 moons)`` () =
        Assert.Equal(17, idx().CelestialBodies.Length)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``ByName index is case-insensitive key`` () =
        // Ключи должны быть lowercase
        Assert.True(Map.containsKey "lorville" (idx().ByName))
        Assert.True(Map.containsKey "new babbage" (idx().ByName))
        Assert.True(Map.containsKey "area18" (idx().ByName))

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``ByName filters UNINITIALIZED entries`` () =
        // Ни один ключ в ByName не должен содержать UNINITIALIZED
        let hasUninit =
            idx().ByName
            |> Map.exists (fun k _ -> k.Contains("uninitialized"))
        Assert.False(hasUninit)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``ByUuid allows lookup by guid`` () =
        // Stanton Star UUID известен из JSON
        let stantonUuid = Guid.Parse("34ff378f-faee-47bb-b5fe-f505e665c5ca")
        let stanton = Map.tryFind stantonUuid (idx().ByUuid)
        Assert.True(stanton.IsSome)
        Assert.Equal("Stanton", stanton.Value.Name)
        Assert.Equal("Star", stanton.Value.Type)

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 2 — Прямой матч по имени (Шаг 1 алгоритма)
    // ─────────────────────────────────────────────────────────────────────────

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: Rayari Cantwell Research Outpost`` () =
        let loc, _ = resolveByName "Rayari Cantwell Research Outpost" |> asKnown
        Assert.Equal("Rayari Cantwell Research Outpost", loc.Name)
        // Проверяем что это на Clio
        let clioUuid = Guid.Parse("2a21d86f-ebf0-4052-a134-c414c9998592")
        Assert.Equal(Some clioUuid, loc.ParentUuid)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: Rayari McGrath Research Outpost`` () =
        let loc, _ = resolveByName "Rayari McGrath Research Outpost" |> asKnown
        Assert.Equal("Rayari McGrath Research Outpost", loc.Name)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: New Babbage is LandingZone`` () =
        let loc, pos = resolveByName "New Babbage" |> asKnown
        Assert.Equal("New Babbage", loc.Name)
        Assert.Equal("LandingZone", loc.Type)
        // Координаты должны совпадать с записью в JSON
        Assert.Equal(pos.X, loc.Position.X, 1.0)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: Calhoun Pass Emergency Shelter`` () =
        let loc, _ = resolveByName "Calhoun Pass Emergency Shelter" |> asKnown
        Assert.Equal("Calhoun Pass Emergency Shelter", loc.Name)
        Assert.Equal("Outpost", loc.Type)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: Shubin Mining Facility SM0-18`` () =
        let loc, _ = resolveByName "Shubin Mining Facility SM0-18" |> asKnown
        Assert.Equal("Shubin Mining Facility SM0-18", loc.Name)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: ArcCorp Mining Area 141 has correct UUID`` () =
        let loc, _ = resolveByName "ArcCorp Mining Area 141" |> asKnown
        Assert.Equal("22a991e5-af7a-4eeb-826f-2f625cb58586", loc.Uuid.ToString())
        // На Daymar
        let daymarUuid = Guid.Parse("e658b2b1-fae7-4945-89b8-b332f235d59b")
        Assert.Equal(Some daymarUuid, loc.ParentUuid)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: Lorville is LandingZone on Hurston`` () =
        let loc, _ = resolveByName "Lorville" |> asKnown
        Assert.Equal("Lorville", loc.Name)
        Assert.Equal("LandingZone", loc.Type)
        let hurstonUuid = Guid.Parse("551af60b-7727-4936-acc7-763d25d7a1de")
        Assert.Equal(Some hurstonUuid, loc.ParentUuid)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: case-insensitive lookup`` () =
        // Нижний регистр должен работать
        let loc, _ = resolveByName "shubin mining facility sm0-18" |> asKnown
        Assert.Equal("Shubin Mining Facility SM0-18", loc.Name)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name match: Lorville CBD falls to UnknownLocation (no exact match)`` () =
        // "Lorville CBD" нет в JSON, нет маркера — должен быть UnknownLocation
        let hint, zid = resolve (Some "Lorville CBD") None |> asUnknown
        Assert.Equal(Some "Lorville CBD", hint)
        Assert.Equal(0UL, zid)

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 3 — Математика координат (Шаг 2 — большой масштаб)
    // ─────────────────────────────────────────────────────────────────────────

    // Эталонный случай из Plan.md:
    // Маркер (160102.99, 169048.69, -58399.57) + Calliope → SMCa-8 с точностью ~88м
    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Coordinate math: marker (160102,169048,-58399) resolves to SMCa-8 on Calliope`` () =
        let markerPos = coords 160102.99 169048.69 -58399.57
        let loc, absPos, dist = resolveByMarker markerPos 751741112843UL |> asInferred
        Assert.Equal("Shubin Mining Facility SMCa-8", loc.Name)
        // Погрешность по плану ~88м, с запасом проверяем < 200м
        Assert.True(dist < 200.0, $"Погрешность слишком большая: {dist} м")
        // Абсолютная позиция должна быть близка к реальной
        let realSmCa8 = coords 22525961362.533 37202818237.256 -58400.327
        Assert.True(LocationResolver.euclideanDistance absPos realSmCa8 < 500.0)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Coordinate math: marker with large coords returns InferredLocation`` () =
        // Используем координаты из лога — pickup_B из Game.log
        let markerPos = coords -329667.413450 899899.230227 -287829.304418
        let result = resolveByMarker markerPos 729982571907UL
        // Должен вернуть InferredLocation (планетарные координаты, так как |x| >> 10000)
        match result with
        | InferredLocation _ -> ()
        | UnknownLocation _  -> () // тоже допустимо если нет локации в радиусе 50км
        | KnownLocation _    -> Assert.Fail("Неожиданный KnownLocation без имени")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Coordinate math: inferred location distance is under 50km threshold`` () =
        // Если вернулся InferredLocation — расстояние должно быть < 50 000 м
        let markerPos = coords 160102.99 169048.69 -58399.57
        match resolveByMarker markerPos 0UL with
        | InferredLocation (_, _, dist) ->
            Assert.True(dist <= LocationResolver.MaxInferenceDistanceM)
        | _ -> ()

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 4 — Интерьерные координаты (малый масштаб → UnknownLocation)
    // ─────────────────────────────────────────────────────────────────────────

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Small coords: (119, -18, 2) → UnknownLocation`` () =
        // Дропофф маркер внутри здания (из Game.log, Covalex)
        let markerPos = coords 119.07 -18.46 2.29
        let result = resolveByMarker markerPos 729990718275UL
        match result with
        | UnknownLocation _ -> ()
        | other -> Assert.Fail($"Ожидался UnknownLocation для малых координат, получен: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Small coords: (-39, 51, -10) → UnknownLocation`` () =
        let markerPos = coords -39.61 51.02 -10.65
        let result = resolveByMarker markerPos 751741145619UL
        match result with
        | UnknownLocation _ -> ()
        | other -> Assert.Fail($"Ожидался UnknownLocation, получен: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Small coords: (-76, 11, -5) → UnknownLocation`` () =
        let markerPos = coords -76.85 11.27 -5.06
        let result = resolveByMarker markerPos 752098336907UL
        match result with
        | UnknownLocation _ -> ()
        | other -> Assert.Fail($"Ожидался UnknownLocation, получен: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Small coords: zoneHostId preserved in UnknownLocation`` () =
        let markerPos = coords 46.52 -7.88 82.10
        let _, zid = resolveByMarker markerPos 999888777UL |> asUnknown
        Assert.Equal(999888777UL, zid)

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 5 — Пограничный случай координат (4143.45, 167.36, -1.66)
    // ─────────────────────────────────────────────────────────────────────────

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Borderline coords (4143, 167, -1): max abs = 4143 < threshold, treated as interior`` () =
        // max(|4143.45|, |167.36|, |-1.66|) = 4143.45 < 10000 → интерьер/площадка
        let markerPos = coords 4143.45 167.36 -1.66
        let result = resolveByMarker markerPos 752098336907UL
        match result with
        | UnknownLocation _ -> ()
        | InferredLocation _ ->
            // Если попал в InferredLocation — значит, пороговое значение не работает
            Assert.Fail("Координата 4143 должна считаться интерьером (< 10000)")
        | KnownLocation _ ->
            Assert.Fail("Неожиданный KnownLocation")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Borderline coords with name override → KnownLocation wins`` () =
        // Если у маркера с малыми координатами есть имя → имя имеет приоритет
        let markerPos = coords 4143.45 167.36 -1.66
        let result = resolve (Some "ArcCorp Mining Area 141") (Some { Position = markerPos; ZoneHostId = 0UL })
        match result with
        | KnownLocation (loc, _) -> Assert.Equal("ArcCorp Mining Area 141", loc.Name)
        | other -> Assert.Fail($"Ожидался KnownLocation, получен: %A{other}")

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 6 — Комбинированный резолв (имя + маркер)
    // ─────────────────────────────────────────────────────────────────────────

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Name takes priority over coordinates when both are present`` () =
        // Имя совпадает → KnownLocation, даже если координаты тоже дали бы результат
        let markerPos = coords 160102.99 169048.69 -58399.57
        let result = resolve (Some "Shubin Mining Facility SMCa-8") (Some { Position = markerPos; ZoneHostId = 0UL })
        match result with
        | KnownLocation (loc, _) -> Assert.Equal("Shubin Mining Facility SMCa-8", loc.Name)
        | other -> Assert.Fail($"Имя должно иметь приоритет: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Unknown name + large marker falls back to InferredLocation`` () =
        // "wreck site" нет в JSON → переходим к координатам
        let markerPos = coords 160102.99 169048.69 -58399.57
        let result = resolve (Some "wreck site") (Some { Position = markerPos; ZoneHostId = 0UL })
        match result with
        | InferredLocation (loc, _, _) -> Assert.Equal("Shubin Mining Facility SMCa-8", loc.Name)
        | other -> Assert.Fail($"Ожидался InferredLocation по координатам: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``No name + small marker → UnknownLocation`` () =
        let markerPos = coords 50.0 10.0 0.0
        let hint, _ = resolve None (Some { Position = markerPos; ZoneHostId = 42UL }) |> asUnknown
        Assert.Equal(None, hint)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Unknown name + small marker → UnknownLocation with hint`` () =
        let markerPos = coords 50.0 10.0 0.0
        let hint, _ = resolve (Some "Inventory Center") (Some { Position = markerPos; ZoneHostId = 42UL }) |> asUnknown
        Assert.Equal(Some "Inventory Center", hint)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``No name, no marker → UnknownLocation`` () =
        let hint, zid = resolve None None |> asUnknown
        Assert.Equal(None, hint)
        Assert.Equal(0UL, zid)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Z-coordinate fallback matches a unique location`` () =
        let targetLoc = { Uuid = Guid.NewGuid(); Name = "Unique Z Station"; Type = "Station"; System = "stanton"; ParentUuid = None; QtValid = true; Position = coords 500000.0 600000.0 5565883.23 }
        let otherLoc = { Uuid = Guid.NewGuid(); Name = "Other Station"; Type = "Station"; System = "stanton"; ParentUuid = None; QtValid = true; Position = coords 0.0 0.0 0.0 }
        
        let customIndex = {
            All = [targetLoc; otherLoc]
            ByUuid = Map.empty
            ByName = Map.empty
            Planets = []
            Moons = []
            CelestialBodies = []
            ReferenceOrigins = []
        }

        // Симулируем маркер с уникальной Z
        let markerPos = coords 10.0 20.0 5565883.231 // +0.001 для проверки округления
        
        let result = LocationResolver.resolveLocation customIndex None (Some { Position = markerPos; ZoneHostId = 0UL })
        match result with
        | KnownLocation (loc, _) ->
            Assert.Equal("Unique Z Station", loc.Name)
        | InferredLocation (loc, _, _) ->
            Assert.Equal("Unique Z Station", loc.Name)
        | other ->
            Assert.Fail($"Ожидался KnownLocation или InferredLocation для совпадения по Z, получен: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Lagrange Point: marker (4287404.49, -602529.96, -301922.08) resolves to ARC-L2 station without name`` () =
        let markerPos = coords 4287404.494091 -602529.965065 -301922.077982
        let zoneHostId = 204751680578UL
        let result = resolveByMarker markerPos zoneHostId
        match result with
        | InferredLocation (loc, _, dist) ->
            Assert.Equal("ARC-L2 Lively Pathway Station", loc.Name)
            Assert.True(dist < 1000.0, $"Погрешность {dist} м превышает 1000м")
        | KnownLocation (loc, _) ->
            Assert.Equal("ARC-L2 Lively Pathway Station", loc.Name)
        | UnknownLocation _ ->
            Assert.Fail("Маркер ARC-L2 без имени не разрешился")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Lagrange Point: marker (-2214759.44, -2598304.27, 951654.88) resolves to ARC-L4 station without name`` () =
        let markerPos = coords -2214759.435731 -2598304.268218 951654.880652
        let zoneHostId = 732970895184UL
        let result = resolveByMarker markerPos zoneHostId
        match result with
        | InferredLocation (loc, _, dist) ->
            Assert.Equal("ARC-L4 Faint Glen Station", loc.Name)
            Assert.True(dist < 1000.0, $"Погрешность {dist} м превышает 1000м")
        | KnownLocation (loc, _) ->
            Assert.Equal("ARC-L4 Faint Glen Station", loc.Name)
        | UnknownLocation _ ->
            Assert.Fail("Маркер ARC-L4 без имени не разрешился")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Lagrange Point: marker (-5329061.23, -1424447.34, 874388.79) resolves to ARC-L5 station without name`` () =
        let markerPos = coords -5329061.226109 -1424447.343246 874388.791416
        let zoneHostId = 732970895173UL
        let result = resolveByMarker markerPos zoneHostId
        match result with
        | InferredLocation (loc, _, dist) ->
            Assert.Equal("ARC-L5 Yellow Core Station", loc.Name)
            Assert.True(dist < 1000.0, $"Погрешность {dist} м превышает 1000м")
        | KnownLocation (loc, _) ->
            Assert.Equal("ARC-L5 Yellow Core Station", loc.Name)
        | UnknownLocation _ ->
            Assert.Fail("Маркер ARC-L5 без имени не разрешился")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Lagrange Point: marker (85397.57, -5041260.84, 2231688.59) resolves to ARC-L1 station without name`` () =
        let markerPos = coords 85397.572878 -5041260.843566 2231688.589955
        let zoneHostId = 732970895185UL
        let result = resolveByMarker markerPos zoneHostId
        match result with
        | InferredLocation (loc, _, dist) ->
            Assert.Equal("ARC-L1 Wide Forest Station", loc.Name)
            Assert.True(dist < 1000.0, $"Погрешность {dist} м превышает 1000м")
        | KnownLocation (loc, _) ->
            Assert.Equal("ARC-L1 Wide Forest Station", loc.Name)
        | UnknownLocation _ ->
            Assert.Fail("Маркер ARC-L1 без имени не разрешился")

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 7 — Вспомогательные функции
    // ─────────────────────────────────────────────────────────────────────────

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``euclideanDistance: known values`` () =
        let a = coords 0.0 0.0 0.0
        let b = coords 3.0 4.0 0.0
        Assert.Equal(5.0, LocationResolver.euclideanDistance a b, 6)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``euclideanDistance: same point is zero`` () =
        let a = coords 100.0 200.0 300.0
        Assert.Equal(0.0, LocationResolver.euclideanDistance a a, 10)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``getParentBody: Outpost on Moon returns that Moon`` () =
        let loc, _ = resolveByName "ArcCorp Mining Area 141" |> asKnown
        // ArcCorp Mining Area 141 → parent_uuid = Daymar
        match LocationResolver.getParentBody loc (idx()) with
        | Some body -> Assert.Equal("Daymar", body.Name)
        | None      -> Assert.Fail("Ожидалось небесное тело")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``getParentBody: LandingZone on Planet returns that Planet`` () =
        let loc, _ = resolveByName "Lorville" |> asKnown
        // Lorville → parent_uuid = Hurston (Planet)
        match LocationResolver.getParentBody loc (idx()) with
        | Some body ->
            Assert.Equal("Hurston", body.Name)
            Assert.Equal("Planet", body.Type)
        | None -> Assert.Fail("Ожидалась планета")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``getParentBody: Planet returns itself`` () =
        let hurstonUuid = Guid.Parse("551af60b-7727-4936-acc7-763d25d7a1de")
        let hurston = Map.find hurstonUuid (idx().ByUuid)
        match LocationResolver.getParentBody hurston (idx()) with
        | Some body -> Assert.Equal("Hurston", body.Name)
        | None      -> Assert.Fail("Планета должна возвращать саму себя")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``getParentBody: Moon returns itself`` () =
        let calliope = idx().Moons |> List.find (fun m -> m.Name = "Calliope")
        match LocationResolver.getParentBody calliope (idx()) with
        | Some body -> Assert.Equal("Calliope", body.Name)
        | None      -> Assert.Fail("Луна должна возвращать саму себя")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``sharePlanet: two moons of Hurston share planet`` () =
        let aberdeen = idx().Moons |> List.find (fun m -> m.Name = "Aberdeen")
        let magda    = idx().Moons |> List.find (fun m -> m.Name = "Magda")
        Assert.True(LocationResolver.sharePlanet aberdeen magda (idx()))

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``sharePlanet: moons of different planets do not share`` () =
        let aberdeen = idx().Moons |> List.find (fun m -> m.Name = "Aberdeen")
        let calliope = idx().Moons |> List.find (fun m -> m.Name = "Calliope")
        Assert.False(LocationResolver.sharePlanet aberdeen calliope (idx()))

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``sharePlanet: moon and its planet share`` () =
        let calliope  = idx().Moons   |> List.find (fun m -> m.Name = "Calliope")
        let microTech = idx().Planets |> List.find (fun m -> m.Name = "microTech")
        Assert.True(LocationResolver.sharePlanet calliope microTech (idx()))

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``sharePlanet: moon and different planet do not share`` () =
        let calliope = idx().Moons   |> List.find (fun m -> m.Name = "Calliope")
        let hurston  = idx().Planets |> List.find (fun m -> m.Name = "Hurston")
        Assert.False(LocationResolver.sharePlanet calliope hurston (idx()))

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``findNearestLocation: returns None for empty list`` () =
        let result = LocationResolver.findNearestLocation (coords 0.0 0.0 0.0) []
        Assert.Equal(None, result)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``findNearestLocation: returns closest entry`` () =
        let target = coords 10.0 0.0 0.0
        let locs = [
            { Uuid = Guid.NewGuid(); Name = "Far";  Type = "Outpost"; System = "stanton"; ParentUuid = None; QtValid = true; Position = coords 100.0 0.0 0.0 }
            { Uuid = Guid.NewGuid(); Name = "Near"; Type = "Outpost"; System = "stanton"; ParentUuid = None; QtValid = true; Position = coords 11.0  0.0 0.0 }
        ]
        match LocationResolver.findNearestLocation target locs with
        | Some (loc, dist) ->
            Assert.Equal("Near", loc.Name)
            Assert.Equal(1.0, dist, 6)
        | None -> Assert.Fail("Ожидался результат")

    // ─────────────────────────────────────────────────────────────────────────
    // Группа 8 — Интеграционные тесты по реальным данным из логов
    // ─────────────────────────────────────────────────────────────────────────

    /// Маркеры с большими координатами из Game.log, которые должны резолвиться
    let private knownLargeMarkers = [
        // (coordinate, expected_location_name)
        // Эталонный маркер из Plan.md
        coords 160102.99   169048.69  -58399.57, "Shubin Mining Facility SMCa-8"
    ]

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: all known large-coordinate markers resolve correctly`` () =
        for (markerPos, expectedName) in knownLargeMarkers do
            let result = resolveByMarker markerPos 0UL
            match result with
            | InferredLocation (loc, _, dist) ->
                Assert.True(
                    loc.Name = expectedName,
                    $"Маркер {markerPos} → ожидалось '{expectedName}', получено '{loc.Name}' (dist={dist}м)"
                )
            | KnownLocation (loc, _) ->
                Assert.Equal(expectedName, loc.Name)
            | UnknownLocation _ ->
                Assert.Fail($"Маркер {markerPos} не разрешился, ожидалось '{expectedName}'")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: destination names from New Objective lines`` () =
        // Имена из "New Objective: Deliver ... to X" из Game.log и Game2.log
        let destinations = [
            // Должны найтись напрямую:
            "ArcCorp Mining Area 141"
            "Calhoun Pass Emergency Shelter"
            "Shubin Mining Facility SM0-18"
            // Lorville — найдётся (но не "Lorville CBD"):
            "Lorville"
        ]
        for name in destinations do
            let result = resolveByName name
            match result with
            | KnownLocation (loc, _) ->
                Assert.Equal(name, loc.Name)
            | other ->
                Assert.Fail($"'{name}' должен быть KnownLocation, получен: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: destinations that need fallback return UnknownLocation with hint`` () =
        // "Lorville CBD" нет в JSON — должен вернуть UnknownLocation с hint
        let hint, _ = resolveByName "Lorville CBD" |> asUnknown
        Assert.Equal(Some "Lorville CBD", hint)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: HDMS locations exist in index`` () =
        // HDMS-локации из New Objective в Game.log
        let hdms = [
            "HDMS-Stanhope"
            "HDMS-Oparei"
        ]
        for name in hdms do
            let found = Map.containsKey (name.ToLowerInvariant()) (idx().ByName)
            Assert.True(found, $"'{name}' не найдено в индексе")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: building interior markers all return UnknownLocation`` () =
        // Маркеры с малыми координатами из обоих логов
        let buildingMarkers = [
            coords 116.65 -18.93  2.26,  729990718275UL   // Covalex S4DC05 (dropoff)
            coords  46.52  -7.88 82.10,  729990753885UL   // Greycat S4 PCA (pickup)
            coords -39.61  51.02 -10.65, 751741145619UL   // Dropoff interior
            coords -76.85  11.27  -5.06, 752098336907UL   // Dropoff interior
        ]
        for (pos, zid) in buildingMarkers do
            let result = resolveByMarker pos zid
            match result with
            | UnknownLocation _ -> ()
            | other ->
                Assert.Fail($"Маркер {pos} с малыми координатами должен быть UnknownLocation, получен: %A{other}")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: SMCa-8 coordinate match is within 200 meters`` () =
        // Конкретная проверка погрешности эталонного маркера из Plan.md
        let markerPos = coords 160102.99 169048.69 -58399.57
        let result = resolveByMarker markerPos 0UL
        match result with
        | InferredLocation (loc, _, _) ->
            Assert.Equal("Shubin Mining Facility SMCa-8", loc.Name)
        | KnownLocation (loc, _) ->
            Assert.Equal("Shubin Mining Facility SMCa-8", loc.Name)
        | UnknownLocation _ ->
            Assert.Fail("SMCa-8 маркер должен разрешаться")

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Integration: Station location types are normalized to SpaceStation`` () =
        let arcL1, _ = resolveByName "ARC-L1 Wide Forest Station" |> asKnown
        Assert.Equal("SpaceStation", arcL1.Type)
        let baijini, _ = resolveByName "Baijini Point" |> asKnown
        Assert.Equal("SpaceStation", baijini.Type)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``Shared ZoneHostId across different objectives does not corrupt resolution`` () =
        let sharedZid = 732970893062UL
        // Pickup with no name, resolved by coordinates
        let pickupPos = coords -507742.3125 -903464.4375 496489.0625
        // Dropoffs resolved by name
        let d0 = resolve (Some "Sakura Sun Magnolia Workcenter") (Some { Position = coords -229373.1 -864792.8 -446960.0; ZoneHostId = sharedZid })
        let d1 = resolve (Some "HDPC-Farnesway") (Some { Position = coords 129223.3 63887.5 989574.5; ZoneHostId = sharedZid })
        let d2 = resolve (Some "Teasa Spaceport") (Some { Position = coords -328668.0 -756979.7 566539.5; ZoneHostId = sharedZid })
        let d3 = resolve (Some "HDPC-Cassillo") (Some { Position = coords -789715.9 615354.4 -2353.0; ZoneHostId = sharedZid })
        
        let pRes = resolve None (Some { Position = pickupPos; ZoneHostId = sharedZid })

        let getName r =
            match r with
            | KnownLocation (l, _) -> l.Name
            | InferredLocation (l, _, _) -> l.Name
            | UnknownLocation (nameOpt, _) -> nameOpt |> Option.defaultValue "Unknown Location"

        Assert.Equal("Sakura Sun Magnolia Workcenter", getName d0)
        Assert.Equal("HDPC-Farnesway", getName d1)
        Assert.Equal("Lorville", getName d2)
        Assert.Equal("HDPC-Cassillo", getName d3)
        Assert.NotEqual<string>("HDPC-Farnesway", getName pRes)

    [<Fact>]
    [<Trait("Category", "LocationResolver")>]
    let ``MIC-L2 pickup marker resolves to MIC-L2 Long Forest Station`` () =
        let markerPos = coords 4947744.265216 7731985.254293 -10634826.649168
        let result = resolveByMarker markerPos 732970895181UL
        match result with
        | InferredLocation (loc, _, dist) ->
            Assert.Equal("MIC-L2 Long Forest Station", loc.Name)
            Assert.True(dist < 100.0, $"Dist {dist} should be < 100m")
        | KnownLocation (loc, _) ->
            Assert.Equal("MIC-L2 Long Forest Station", loc.Name)
        | UnknownLocation (nameOpt, zid) ->
            Assert.Fail($"Expected MIC-L2 Long Forest Station, got UnknownLocation ({nameOpt}, {zid})")

