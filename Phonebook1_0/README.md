# Phone Book 1.0

## Description

Phone Book 1.0 is an Android mobile application built with C# and .NET MAUI. It allows users to add and view contacts with a name, phone number, and optional profile picture specified by a URL.

## Features

- View contacts in a scrollable list.
- Add contacts on a separate screen.
- Enter a contact's name and phone number.
- Optionally provide a profile picture URL.
- Display a default image when no picture is provided or the supplied image cannot be displayed.
- Validate that name and phone number fields are not empty.
- Validate the format of an optional HTTP/HTTPS picture URL.
- Navigate between the contact list and the add-contact form.
- Automatically update the contact list after adding a contact.

The application **does not perform country-specific phone number validation**.

## Technologies

- C# and .NET 10
- .NET MAUI and XAML
- Android
- `ObservableCollection<Contact>` for in-memory contact data
- JetBrains Rider for development and execution

## Project Structure

| File | Purpose |
| --- | --- |
| `Models/Contact.cs` | Contact model: name, phone number, and picture URL |
| `App.xaml` / `App.xaml.cs` | Application resources, shared contact collection, and initial navigation |
| `MainPage.xaml` / `MainPage.xaml.cs` | Contact list and navigation to the add-contact form |
| `AddPage.xaml` / `AddPage.xaml.cs` | Add-contact form, input validation, and contact creation |
| `AppShell.xaml` / `AppShell.xaml.cs` | Unused files retained from the original MAUI template |
| `MauiProgram.cs` | MAUI application configuration |

## Prerequisites

### Linux Development Environment

- .NET 10 SDK installed.
- .NET MAUI Android workload installed.
- Android SDK and a compatible JDK configured in JetBrains Rider.
- An Android emulator or a physical Android device with developer options enabled.

### Running and Testing on Windows 11

This application targets **Android**. Windows 11 serves as the host operating system for building and running the app through an Android emulator or a connected Android device. The project **does not provide a standalone Windows executable (`.exe`)**.

Requirements:

1. Windows 11.
2. .NET 10 SDK.
3. JetBrains Rider with Android/.NET MAUI support.
4. Android SDK and a compatible JDK.
5. .NET MAUI Android workload.
6. An Android emulator or a physical Android device.

Running an emulator may require hardware virtualization to be enabled and correctly configured on Windows.

## Installation and Build

1. Download or extract the project.
2. Open `Phonebook1_0.csproj` in JetBrains Rider.
3. Verify the Android SDK and JDK paths.
4. Ensure that the required Android workload is installed.
5. Open a terminal in the directory containing the `.csproj` file and run:

```bash
dotnet workload restore
dotnet restore
dotnet build -f net10.0-android
```

If the project file specifies a different Android target framework, use that value instead of `net10.0-android`.

## Running the Application

1. Start an Android emulator or connect a compatible Android device.
2. Open the project in JetBrains Rider.
3. Select the Android run configuration and target device.
4. Click **Run**.
5. Once the app launches, the main screen displays the contact list.

## Usage

1. Tap **Add Contact** on the main screen.
2. Enter a name and phone number.
3. Optionally enter a complete HTTP/HTTPS image URL.
4. Tap **Add Contact** to save the contact for the current session.
5. The application returns to the contact list and displays the new contact.

When no picture URL is supplied, the application uses a default image loaded from Unsplash. If a custom picture cannot be displayed, the underlying default image may remain visible. Both images depend on an internet connection and the availability of their respective URLs.

## Functional Testing

| Scenario | Expected Result |
| --- | --- |
| Launch with no contacts | An empty list and an explanatory message are displayed |
| Tap **Add Contact** | The new-contact form opens |
| Submit with an empty name or phone number | An error message appears; no contact is added |
| Enter an invalid picture URL format | An error message appears |
| Enter a name and phone number without a picture URL | Contact is added; the default picture is used |
| Enter a valid picture URL | The application attempts to load the specified picture |
| Add multiple contacts | All contacts appear in the list |
| Navigate back from the form | No contact is added |
| Restart the application | Contacts from the previous session are not retained |

## Known Limitations

- Contacts are stored only in memory; the list is empty after restarting the app.
- Editing, deleting, and searching for contacts are not supported.
- Phone numbers are not checked against national numbering plans or verified as existing numbers.
- Picture URLs are checked for valid format, but the application does not verify that the server actually returns an image.
- The default image is loaded online and is not guaranteed to appear without an internet connection.
- There is no standalone Windows version.
- Running the project on Windows 11 has **not yet been validated on an actual Windows 11 system**.

## Default Image

The default contact image is provided through Unsplash:

https://images.unsplash.com/photo-1740252117012-bb53ad05e370