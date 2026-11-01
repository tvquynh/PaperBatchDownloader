# Timed Mouse Clicker 1.0.0

A standalone Windows x64 utility that sends one left mouse click to a chosen screen position after a configurable delay. It runs separately from Paper Batch Downloader.

## Download and use

Download `TimedMouseClicker-v1.0.0-win-x64.zip` from the [utility release](https://github.com/tvquynh/PaperBatchDownloader/releases/tag/clicker-v1.0.0), extract it and open `TimedMouseClicker.exe`. The .NET runtime is included.

1. Press the position-capture button, then move the pointer to the desired target within three seconds. Capturing the position does not click.
2. Set a delay and choose seconds or minutes. The default is 15 seconds.
3. Press Start. The countdown begins; when it ends, the pointer moves to the saved coordinates and sends one left click.
4. Press **F8** from any application or the Cancel button to cancel. Closing the utility also cancels the pending schedule.

Keep the target window in the same position after capturing coordinates. Each schedule clicks only once; press Start again to schedule another click. The controls are in Vietnamese. If another application owns F8, the utility refuses to arm the timer and displays a message.

## Build

On Windows with the .NET 8 SDK:

```powershell
dotnet publish TimedMouseClicker/TimedMouseClicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o TimedMouseClicker/publish
```

The Release build succeeded. Automated end-to-end mouse-click testing has not been performed.

## Local behavior

The utility has no network calls, screen-content recognition or persistent settings. It stores the selected coordinates and active countdown in memory while open. Windows may prevent synthetic input into elevated or protected applications. Distributed under the repository's MIT license; bundled runtime notices are included in the Windows ZIP.
