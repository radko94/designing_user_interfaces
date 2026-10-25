# HTML Downloader 2.0

## Purpose and functionality

HTML Downloader 2.0 is an Android app (C#, .NET 10, .NET MAUI) that downloads a web page from a URL and displays its extracted body text. It is a MAUI adaptation of the lecturer's Chapter 2 **"HTML Downloader 2.0"** Xamarin.Forms exercise: enter a URL and press **Download**; the page's HTML is fetched and parsed with AngleSharp, and the plain text of the body is shown.

Beyond the original, it validates the URL, shows a loading indicator, and reports clear errors for network failures, timeouts, and processing problems.

## Technology stack

- C# on .NET 10 (`net10.0-android`)
- .NET MAUI (XAML)
- Packages: `AngleSharp 1.8.6-beta.868` (HTML parsing), `Microsoft.Maui.Controls`, `Microsoft.Extensions.Logging.Debug`
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
3. Open a terminal in the `HtmlDownloader2_0/` folder (the one containing `HtmlDownloader2_0.csproj`) and run:

```bash
dotnet restore
dotnet build -f net10.0-android -c Debug
```

## How to launch

1. Start an Android emulator or connect an Android device.
2. Deploy and run the app (in Rider, select the Android target and run).
3. Enter a URL (an HTTPS URL is pre-filled) and press **Download**.

Install an APK only if one has actually been produced and signed; no APK is shipped by default.

## How to test

| Scenario | Expected result |
|---|---|
| Valid HTTPS URL (default) | Body text appears in the output area |
| Empty or malformed URL | "Invalid URL" message |
| Offline / unreachable host | "Network error" message |
| Server slow to respond | "Timeout" message after ~20 seconds |

## Known limitations

- Android is the only intended target; there is no native Windows executable.
- Only HTTPS and HTTP URLs are accepted. Cleartext `http://` may be blocked by Android 9+ (API 28) network policy depending on the target configuration.
- Execution on Windows 11 has not yet been independently verified (pending).

## Source attribution

- Lecturer's course book (GitBook): https://dimitar-minchev.gitbook.io/developing-cross-platform-apps/
- Original "HTML Downloader 2.0" exercise (Chapter 2, Xamarin.Forms): https://raw.githubusercontent.com/dimitarminchev/DCPA/Bulgarian/chapter2/209_HTML_Downloader_2.0.md
- Course examples repository (DCPA): https://github.com/dimitarminchev/DCPA
- AngleSharp: https://anglesharp.github.io/
- .NET MAUI documentation: https://learn.microsoft.com/dotnet/maui/
