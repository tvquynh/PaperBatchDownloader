# Timed Mouse Clicker 1.1.0

A standalone Windows x64 utility that sends repeated left mouse clicks to a chosen screen position after a configurable delay. It runs separately from Paper Batch Downloader.

## Download and use

Download `TimedMouseClicker-v1.1.0-win-x64.zip` from the [utility release](https://github.com/tvquynh/PaperBatchDownloader/releases/tag/clicker-v1.1.0), extract it and open `TimedMouseClicker.exe`. The .NET runtime is included.

1. Press the position-capture button, then move the pointer to the desired target within three seconds. Capturing the position does not click.
2. Set the interval between clicks and choose seconds or minutes. The default is 15 seconds.
3. Enter a finite click count (1–1,000,000), or enable **Repeat forever** to keep clicking until stopped.
4. Press Start. The first click waits for the full interval, then the tool repeats at the chosen interval. The status shows completed clicks and the next countdown.
5. Press **F8** from any application or the Cancel button to cancel. Closing the utility also cancels the pending schedule.

Keep the target window in the same position after capturing coordinates. A finite schedule stops at its chosen count. **Repeat forever** runs until F8, Cancel or closing the utility. The default count is 1. Each new schedule starts its count at zero. Missed time is not replayed as a burst of clicks. The controls are in Vietnamese. If another application owns F8, the utility refuses to arm the timer and displays a message.

## Build

On Windows with the .NET 8 SDK:

```powershell
dotnet publish TimedMouseClicker/TimedMouseClicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o TimedMouseClicker/publish
```

The Release build succeeded. Counting checks passed for limits 1, 3 and 1,000,000, rejection of extra clicks, indefinite repetition, fresh restart and invalid zero counts. Automated end-to-end mouse-click testing has not been performed.

## Local behavior

The utility has no network calls, screen-content recognition or persistent settings. It stores the selected coordinates and active countdown in memory while open. Windows may prevent synthetic input into elevated or protected applications. Distributed under the repository's MIT license; bundled runtime notices are included in the Windows ZIP.
