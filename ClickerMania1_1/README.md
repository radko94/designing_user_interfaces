# Clicker Mania 1.1

## Purpose and functionality

Clicker Mania 1.1 is an Android click-counter app (C#, .NET 10, .NET MAUI). It is a MAUI adaptation of the lecturer's Chapter 1 **"Clicker Mania 1.1"** UWP exercise: each press of the **Click** button increases the counter by one. The total is saved to a local `clicker.txt` file and restored on the next launch.

Beyond the original, it guards against overflow, validates the saved value, disables the button during file I/O, and reports read/save errors.

## Technology stack

- C# on .NET 10 (`net10.0-android`)
- .NET MAUI (XAML)
- Packages: `Microsoft.Maui.Controls`, `Microsoft.Extensions.Logging.Debug`
- Persistence: `FileSystem.Current.AppDataDirectory` (`clicker.txt`)

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
3. Open a terminal in the `ClickerMania1_1/` folder (the one containing `ClickerMania1_1.csproj`) and run:

```bash
dotnet restore
dotnet build -f net10.0-android -c Debug
```

## How to launch

1. Start an Android emulator or connect an Android device.
2. Deploy and run the app (in Rider, select the Android target and run).
3. The app opens with an orange background, a counter, and a **Click** button.

## How to test

| Test | Expected behavior |
|---|---|
| First launch (no saved data) | Counter shows `0` |
| Press **Click** once | Counter shows `1` |
| Press **Click** five more times | Counter shows `6` |
| Close and relaunch the app | Counter still shows `6` |
| Saved file contains invalid text | Warning shown; counter starts at `0` |

For the persistence test, close and relaunch without clearing app storage or uninstalling.

## Known limitations

- Android is the only intended target; there is no native Windows executable.
- The counter is limited to the range of a signed 32-bit integer.
- Clearing app storage or uninstalling removes the saved counter.
- Execution on Windows 11 has not yet been independently verified (pending).

## Source attribution

- Lecturer's course book (GitBook): https://dimitar-minchev.gitbook.io/developing-cross-platform-apps/
- Original "Clicker Mania 1.1" exercise (Chapter 1, UWP): https://raw.githubusercontent.com/dimitarminchev/DCPA/Bulgarian/chapter1/114_Clicker_Mania_1.1.md
- Course examples repository (DCPA): https://github.com/dimitarminchev/DCPA
- .NET MAUI file system helpers: https://learn.microsoft.com/dotnet/maui/platform-integration/storage/file-system-helpers
