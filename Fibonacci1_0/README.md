
# Fibonacci 1.0

## Description

Fibonacci 1.0 is a C#/.NET MAUI Android application
that generates Fibonacci numbers below a user-defined
positive integer limit.

The application is based on the Fibonacci 1.0 exercise
from the Design of User Interface course at
Burgas Free University.

## Technologies

- C#
- .NET 10
- .NET MAUI
- XAML
- Android
- JetBrains Rider

## Features

- Numeric input with default limit 1000
- Fibonacci sequence generation
- Scrollable results
- Input validation
- Prevention of integer overflow
- Automatic clearing of previous results

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
2. Enter 100 in the Limit field.
3. Press Generate.
4. Verify the sequence ends with 89.
5. Enter 1000 and regenerate.
6. Verify the sequence ends with 987.
7. Verify that repeated generation clears old results.
8. Enter invalid input and verify error handling.

## Known Limitations

- Android is the primary target platform.
- A compatible Android environment is required.
- Only positive 32-bit integer limits are accepted.
- Windows 11 execution has not yet been independently
  validated.

## Reference

Original Fibonacci 1.0 exercise:

https://dimitar-minchev.gitbook.io/developing-cross-platform-apps/101_intorduction/109_applications/111_fibonacci_1.0
