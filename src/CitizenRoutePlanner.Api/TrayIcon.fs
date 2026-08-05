module CitizenRoutePlanner.Api.TrayIcon

open System
open System.Drawing
open System.Windows.Forms
open System.Threading
open System.Diagnostics
open System.Net
open System.Net.Sockets
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection

let getLocalIp () =
    try
        use socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0)
        socket.Connect("8.8.8.8", 65530)
        let endPoint = socket.LocalEndPoint :?> IPEndPoint
        endPoint.Address.ToString()
    with _ ->
        "127.0.0.1"

let getPort (app: WebApplication) =
    try
        let server = app.Services.GetRequiredService<IServer>()
        let addresses = server.Features.Get<IServerAddressesFeature>()
        if addresses <> null && addresses.Addresses.Count > 0 then
            let address = addresses.Addresses |> Seq.head
            let lastColon = address.LastIndexOf(':')
            if lastColon > -1 then
                let portStr = address.Substring(lastColon + 1).TrimEnd('/')
                match Int32.TryParse(portStr) with
                | true, p -> p
                | _ -> 5000
            else
                80
        else
            5000 // default
    with _ -> 5000

let openBrowser url =
    try
        Process.Start(ProcessStartInfo(url, UseShellExecute = true)) |> ignore
    with ex ->
        printfn "Failed to open browser: %s" ex.Message

let copyToClipboard text =
    try
        Clipboard.SetText(text)
    with ex ->
        printfn "Failed to copy to clipboard: %s" ex.Message

let run (app: WebApplication) (appLifetime: Microsoft.Extensions.Hosting.IHostApplicationLifetime) =
    let t = new Thread(fun () ->
        try
            Application.SetHighDpiMode(HighDpiMode.SystemAware)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(false)

            use trayIcon = new NotifyIcon()
            trayIcon.Text <- "Citizen Route Planner"
            
            // Create a default icon (e.g. system shield or just draw something simple)
            let bmp = new Bitmap(16, 16)
            use g = Graphics.FromImage(bmp)
            g.Clear(Color.DarkOrange)
            g.DrawEllipse(Pens.White, 2, 2, 12, 12)
            trayIcon.Icon <- Icon.FromHandle(bmp.GetHicon())
            
            let contextMenu = new ContextMenuStrip()

            let port = getPort app

            let openBrowserItem = new ToolStripMenuItem("Открыть интерфейс в браузере")
            openBrowserItem.Click.Add(fun _ -> 
                openBrowser (sprintf "http://localhost:%d" port)
            )
            
            let copyLinkItem = new ToolStripMenuItem("Скопировать ссылку для другого устройства")
            copyLinkItem.Click.Add(fun _ ->
                let ip = getLocalIp ()
                copyToClipboard (sprintf "http://%s:%d" ip port)
            )

            let exitItem = new ToolStripMenuItem("Выйти")
            exitItem.Click.Add(fun _ -> 
                trayIcon.Visible <- false
                Application.Exit()
                appLifetime.StopApplication()
            )

            contextMenu.Items.Add(openBrowserItem) |> ignore
            contextMenu.Items.Add(copyLinkItem) |> ignore
            contextMenu.Items.Add(new ToolStripSeparator()) |> ignore
            contextMenu.Items.Add(exitItem) |> ignore

            trayIcon.ContextMenuStrip <- contextMenu
            trayIcon.Visible <- true

            Application.Run()
        with ex ->
            printfn "Tray Icon error: %s" ex.Message
    )
    t.SetApartmentState(ApartmentState.STA)
    t.IsBackground <- true
    t.Start()
