# Clicker Mania 1.2

## Purpose and functionality

Clicker Mania 1.2 is an Android timed-clicking app (C#, .NET 10, .NET MAUI). It is a MAUI adaptation of the lecturer's Chapter 1 **"Clicker Mania 1.2"** UWP exercise: a one-second timer counts elapsed time, the user clicks a button to count clicks, and the app shows the elapsed seconds, the click count, and the average **clicks per minute** (CPM).

The timer runs while the page is visible and stops when it is hidden; the CPM value is formatted to two decimal places.

## Technology stack

- C# on .NET 10 (`net10.0-android`)
- .NET MAUI (XAML)
- Packages: `Microsoft.Maui.Controls`, `Microsoft.Extensions.Logging.Debug`
- Timer: `IDispatcherTimer` (`Microsoft.Maui.Dispatching.Dispatcher.CreateTimer`)

## Prerequisites

This is an **Android-targeted** application, evaluated on **Windows 11** via an Android emulator or device.

- Windows 11
- .NET 10 SDK with the `maui-android` workload
- JDK 21 (the .NET 10 Android workload requires JDK 21)
- Android SDK
- An Android emulator or physical device

## Windows 11 installation / build instructions

1. Install the .NET 10 SDK and the `maui-android` workload.
2. Install JDK 21 and the Android SDK, and point the tooling at them.
3. Open a terminal in the `ClickerMania1_2/` folder (the one containing `ClickerMania1_2.csproj`) and run:

```bash
dotnet restore
dotnet build -f net10.0-android -c Debug
```

## How to launch

1. Start an Android emulator or connect an Android device.
2. Deploy and run the app (in Rider, select the Android target and run).
3. The app opens with a lime background and the Timer / Clicks / Clicks Per Minute readouts.

## How to test

| Scenario | Expected behavior |
|---|---|
| Launch the app | Timer starts counting up from 0 |
| Press **Click** a few times | Clicks counter increases; CPM updates |
| Wait 60 seconds with no clicks | CPM trends toward 0.00 |
| Leave the page (navigate away) | Timer stops |
| Return to the page | Timer resumes |

## Known limitations

- Android is the only intended target; there is no native Windows executable.
- No persistence: the counters reset when the app is relaunched (consistent with the original exercise).
- Execution on Windows 11 has not yet been independently verified (pending).

## Source attribution

- Lecturer's course book (GitBook): https://dimitar-minchev.gitbook.io/developing-cross-platform-apps/
- Original "Clicker Mania 1.2" exercise (Chapter 1, UWP): https://raw.githubusercontent.com/dimitarminchev/DCPA/Bulgarian/chapter1/115_Clicker_Mania_1.2.md
- Course examples repository (DCPA): https://github.com/dimitarminchev/DCPA
- .NET MAUI documentation: https://learn.microsoft.com/dotnet/maui/
