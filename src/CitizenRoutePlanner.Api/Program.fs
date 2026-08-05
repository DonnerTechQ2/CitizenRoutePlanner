open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open CitizenRoutePlanner.Api.Services
open CitizenRoutePlanner.Api.Hubs

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    builder.Services.AddSignalR()
        .AddJsonProtocol(fun options ->
            options.PayloadSerializerOptions.PropertyNamingPolicy <- System.Text.Json.JsonNamingPolicy.CamelCase
            options.PayloadSerializerOptions.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
        ) |> ignore
    builder.Services.AddSingleton<AppStateService>() |> ignore
    builder.Services.AddHostedService<LogWatcherService>() |> ignore

    builder.Services.AddCors(fun options ->
        options.AddDefaultPolicy(fun policy ->
            policy.AllowAnyHeader()
                  .AllowAnyMethod()
                  .SetIsOriginAllowed(fun _ -> true)
                  .AllowCredentials() |> ignore
        )
    ) |> ignore

    let app = builder.Build()

    app.UseCors() |> ignore

    // Serve static files for Svelte frontend from wwwroot
    app.UseDefaultFiles() |> ignore
    app.UseStaticFiles() |> ignore

    app.MapHub<RouteHub>("/hub/route") |> ignore

    // Debug endpoint for injecting log lines
    app.MapPost("/api/debug/inject-log-line", Func<HttpContext, System.Threading.Tasks.Task<IResult>>(fun context ->
        task {
            use reader = new StreamReader(context.Request.Body)
            let! line = reader.ReadToEndAsync()
            
            let stateService = context.RequestServices.GetRequiredService<AppStateService>()
            stateService.InjectLine(line)
            
            return Results.Ok()
        }
    )) |> ignore

    let lifetime = app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>()
#if WINDOWS
    CitizenRoutePlanner.Api.TrayIcon.run app lifetime
#endif

    app.Run()

    0
