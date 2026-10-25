# Suma 4.0

## Purpose and functionality

Suma 4.0 is a small two-number sum calculator for Android, built with C#, .NET 10, and .NET MAUI. It implements the lecturer's Chapter 4 **"Suma 4.0"** exercise, which is already a MAUI exercise: the user enters two integers (A and B) and presses **Sum**; the app displays the result (A + B) in a read-only result field.

This page uses the platform/theme defaults — **no background color** is set, matching the original MAUI example (unlike the Chapter 3 WinUI "Suma 3.0" predecessor, which used an orange background).

Beyond the original, it validates input and handles overflow: non-integer input shows an error message (both an in-page label and an alert dialog), and a result that exceeds the 32-bit integer range is reported instead of crashing or silently wrapping.

## Technology stack

- C# on .NET 10 (`net10.0-android`)
- .NET MAUI (XAML)
- Packages: `Microsoft.Maui.Controls`, `Microsoft.Extensions.Logging.Debug`
- Android manifest declares `android.permission.INTERNET` and `android.permission.ACCESS_NETWORK_STATE` (template defaults; unused by this app)

## Prerequisites

This is an **Android-targeted** application, not a native Windows program. It is evaluated on **Windows 11**, where it runs inside an Android emulator or on a connected Android device.

- Windows 11
- .NET 10 SDK with the `maui-android` workload
- JDK 21 (the .NET 10 Android workload requires JDK 21; newer JDKs such as 26 are rejected)
- Android SDK
- An Android emulator or physical device

## Windows 11 installation / build instructions

1. Install the .NET 10 SDK and the `maui-android` workload.
2. Install JDK 21 and the Android SDK, and point the tooling at them.
3. Open a terminal in the `Suma4_0/` folder (the one containing `Suma4_0.csproj`) and run:

```bash
dotnet restore
dotnet build -f net10.0-android -c Debug
```

## How to launch

1. Start an Android emulator or connect an Android device.
2. Deploy and run the app (in Rider, select the Android target and run; or use `dotnet build -t:Run -f net10.0-android`).
3. The app opens with the default theme, a "Suma 4.0" title, and the A / B / A+B fields.

Install an APK only if one has actually been produced and signed; no APK is shipped by default.

## How to test

| A | B | Expected result |
|---|---|---|
| `5` | `7` | `12` |
| `-3` | `10` | `7` |
| `0` | `0` | `0` |
| `abc` | `5` | error message (invalid input) |
| `2147483647` | `1` | error message (integer overflow) |

The result field (`A+B=`) is read-only; the user can only type into the A and B fields.

## Known limitations

- Android is the only intended target; there is no native Windows executable.
- Input is limited to 32-bit integers.
- The result field is read-only (a documented departure from the original editable `Entry`).
- Execution on Windows 11 has not yet been independently verified (pending).

## Source attribution

- Lecturer's course book (GitBook): https://dimitar-minchev.gitbook.io/developing-cross-platform-apps/
- Original "Suma 4.0" exercise (Chapter 4, MAUI): https://raw.githubusercontent.com/dimitarminchev/DCPA/Bulgarian/chapter4/405_Suma_4.0.md
- Course examples repository (DCPA): https://github.com/dimitarminchev/DCPA
- .NET MAUI documentation: https://learn.microsoft.com/dotnet/maui/
