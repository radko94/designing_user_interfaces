
# Fibonacci 2.0

## Description

Fibonacci 2.0 is a C#/.NET MAUI Android application
that generates Fibonacci numbers below a user-defined
positive integer limit.

The application is based on the Fibonacci 2.0 exercise
(Chapter 2, originally Xamarin.Forms) from the
Design of User Interface course at
Burgas Free University.

Unlike Fibonacci 1.0, this adaptation specifies no
background color and therefore uses the default
.NET MAUI theme.

## Technologies

- C#
- .NET 10
- .NET MAUI
- XAML
- Android
- JetBrains Rider

## Features

- Numeric input with default limit 1000
- Fibonacci sequence generation starting at 1, 1, 2, 3, 5, ...
- Scrollable results list (CollectionView)
- Input validation
- Prevention of integer overflow
- Fresh results on every Generate click (results replace, never accumulate)
- Default MAUI theme (no background color)

## Development Environment

- Manjaro Linux
- JetBrains Rider
- .NET SDK 10
- .NET MAUI Android workload
- Android SDK and Android emulator

## Running on Windows 11

This application targets Android. It does not
currently provide a native Windows executable.

Prerequisites:

1. Windows 11
2. .NET 10 SDK
3. JetBrains Rider with Android development support
4. Android SDK and compatible JDK
5. .NET MAUI Android workload
6. Android emulator or physical Android device

Setup:

1. Extract the project ZIP.
2. Open the project in JetBrains Rider.
3. Restore the NuGet dependencies.
4. Start an Android emulator.
5. Select the Android run configuration.
6. Build and launch the application.

CLI build command:

    dotnet build -f net10.0-android

Use the target framework specified in the project
file if it differs.

## Testing

1. Launch the application.
2. Verify the title "Fibonacci 2.0" and the "Limit"
   field (pre-filled with 1000) are shown.
3. Press Generate.
4. Verify the sequence is
   1, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233,
   377, 610, 987.
5. Enter 100 in the Limit field and press Generate.
6. Verify the sequence ends with 89.
7. Press Generate again and verify the results are
   replaced, not duplicated.
8. Enter invalid input (empty, text, zero, negative)
   and verify an error message is shown.

## Known Limitations

- Android is the primary target platform.
- A compatible Android environment is required.
- Only positive 32-bit integer limits are accepted.
- Windows 11 execution has not yet been independently
  validated.

## Reference

Original Fibonacci 2.0 exercise (Chapter 2, Xamarin.Forms):

https://raw.githubusercontent.com/dimitarminchev/DCPA/Bulgarian/chapter2/205_Fibonacci_2.0.md
