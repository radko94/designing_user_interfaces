# RSS Reader 1.0

## Purpose and functionality

RSS Reader 1.0 is an Android app (C#, .NET 10, .NET MAUI) that reads a news feed (RSS) from a URL and shows its items. It is a MAUI adaptation of the lecturer's Chapter 1 **"RSS Reader 1.0"** UWP exercise: a feed URL is pre-filled, pressing **Download** fetches and parses the feed, and each item is shown as a tappable title with its published date underneath. Tapping a title opens the item's link in the browser.

Beyond the original, it validates the URL, clears the list before each reload (no duplicate accumulation), shows a loading indicator, and reports clear errors for invalid URLs, network failures, timeouts, and malformed feeds.

## Technology stack

- C# on .NET 10 (`net10.0-android`)
- .NET MAUI (XAML)
- `HttpClient` + `System.Xml.Linq` (RSS 2.0 parsing) — both built into .NET, no extra NuGet package
- `Launcher.Default.OpenAsync` (MAUI Essentials) to open item links
- Android permissions: `android.permission.INTERNET` and `android.permission.ACCESS_NETWORK_STATE`

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
3. Open a terminal in the `RssReader1_0/` folder (the one containing `RssReader1_0.csproj`) and run:

```bash
dotnet restore
dotnet build -f net10.0-android -c Debug
```

## How to launch

1. Start an Android emulator or connect an Android device.
2. Deploy and run the app (in Rider, select the Android target and run).
3. The app opens with a lime background, a "RSS Reader 1.0" title, a URL field, and a **Download** button.

## How to test

| Scenario | Expected result |
|---|---|
| Default URL, press **Download** | Feed items appear (title + date) |
| Tap an item's title | The item's link opens in the browser |
| Empty/malformed URL | "Invalid URL" message |
| Offline / unreachable host | "Network error" message |
| URL that is not an RSS feed | "Error" message (parse failure) |

Note: the default feed `https://www.minchev.eu/feed/` is the lecturer's example; its availability was not verified, so an alternative public RSS 2.0 feed may be used for testing.

## Known limitations

- Android is the only intended target; there is no native Windows executable.
- Parses RSS 2.0 `<item>` feeds; Atom feeds are not handled.
- Only HTTP/HTTPS URLs are accepted. Cleartext `http://` may be blocked by Android 9+ (API 28) network policy depending on target configuration.
- Execution on Windows 11 has not yet been independently verified (pending).
