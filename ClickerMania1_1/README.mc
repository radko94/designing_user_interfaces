# Clicker Mania 1.1

## Overview

Clicker Mania 1.1 is a simple Android click-counter application developed using C#, .NET 10 and .NET MAUI. Each press of the **Click** button increases the counter by one. The total is saved in a local `clicker.txt` file and restored on the next launch.

## Features

- Large, centered click counter with an orange background.
- **Click** button increments the count by one.
- Saves the count after each successful press.
- Restores the saved count when the application is relaunched.
- Initializes the counter to zero when no save file exists.
- Handles invalid saved values and common file-access errors.
- Prevents overlapping writes by disabling the button while saving.

## Technologies

- C# and .NET 10
- .NET MAUI and XAML
- Android
- .NET file I/O (`File.ReadAllTextAsync` and `File.WriteAllTextAsync`)
- MAUI application storage (`FileSystem.Current.AppDataDirectory`)
- JetBrains Rider

## Project structure

| File | Responsibility |
| --- | --- |
| `MainPage.xaml` | Main screen layout, counter, and button |
| `MainPage.xaml.cs` | Counter management and local file persistence |
| `App.xaml` / `App.xaml.cs` | MAUI application setup (generated template) |
| `AppShell.xaml` / `AppShell.xaml.cs` | Shell navigation (generated template) |
| `MauiProgram.cs` | MAUI application bootstrap and configuration |
| `ClickerMania1_1.csproj` | Build targets and project configuration |

## Development environment

The application is developed on Manjaro Linux using JetBrains Rider, .NET 10, .NET MAUI Android workload, Android SDK, a compatible JDK, and an Android emulator.

## Prerequisites on Windows 11

This is an **Android-targeted application**, not a native Windows `.exe`. On Windows 11 it runs inside an Android emulator or on a connected Android device.

Install and configure:

1. Windows 11 and the .NET 10 SDK.
2. JetBrains Rider with .NET MAUI/Android support.
3. A compatible JDK and Android SDK.
4. The .NET MAUI Android workload.
5. An Android emulator or physical Android device. If using an emulator, enable and configure hardware virtualization as required by your system.

The exact Android SDK and JDK paths depend on the Windows installation.

## Build instructions

1. Extract the application source archive.
2. Open `ClickerMania1_1.csproj` in Rider.
3. Configure the Android SDK and JDK paths, and start an emulator or connect a device.
4. Run the following commands from the directory containing the project file:

```bash
dotnet workload restore
dotnet restore
dotnet build -f net10.0-android
```

If the project's `<TargetFrameworks>` value differs, use the Android target framework declared in the `.csproj` file.

## Launch instructions

1. Start the Android emulator or connect an Android device.
2. Select the Android target in Rider's run configuration.
3. Run the project through Rider.
4. The application should open with an orange background, a counter, and a **Click** button.

## Testing

| Test | Expected behavior |
| --- | --- |
| First launch with no existing data | The counter displays `0` |
| Press **Click** once | The counter displays `1` |
| Press **Click** five more times | The counter displays `6` |
| Close the app completely and relaunch | The counter still displays `6` |
| Press **Click** again | The counter displays `7` and saves the value |
| Saved file contains invalid or negative text | The app shows a warning and starts at `0` |
| Saved file cannot be read or written | The app shows an appropriate error alert |

For a persistence test, close and relaunch the application **without clearing its storage or uninstalling it**. If testing on an emulator, keep the same emulator/device and installed application between launches.

## Saved data

The counter is stored as text in a file named `clicker.txt` inside the application's private data directory (`FileSystem.Current.AppDataDirectory`). The location is provided by MAUI and must not be hard-coded. The file is created automatically after the first successful click.

## Known limitations

- Android is the only intended target for this exercise. There is no native Windows executable.
- The counter is limited to the range of a signed 32-bit integer.
- The button is briefly disabled during each file write, so presses made while saving are not counted.
- Clearing app storage or uninstalling the app may remove the saved counter.
- Unexpected process termination or storage failure may prevent the most recent update from being saved.
- Execution on Windows 11 has not yet been independently verified.

## Technical documentation

- [.NET MAUI documentation](https://learn.microsoft.com/dotnet/maui/)
- [.NET MAUI file system helpers](https://learn.microsoft.com/dotnet/maui/platform-integration/storage/file-system-helpers?view=net-maui-10.0)