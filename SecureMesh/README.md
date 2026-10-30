# SecureMesh — Encrypted Bluetooth Messenger

## Purpose and functionality

SecureMesh is an **offline-first, peer-to-peer messenger for Android** built with C# / .NET 10 / .NET MAUI. It lets two nearby phones exchange **end-to-end-encrypted text messages over Bluetooth with no internet and no central server**, using an in-person **QR-code** exchange to verify identities.

It is inspired by **Bridgefy** and **Briar**, but its differentiator is **real, vetted cryptography** — Bridgefy's own encryption was broken twice; SecureMesh uses standard, proven primitives.

**Current status:** 1:1 and group text chat are both implemented; the core is fully unit/integration-tested and the app builds and runs on the Android emulator. Voice/video are intentionally out of scope.

## Technology stack

- C# on .NET 10 (`net10.0-android`), .NET MAUI (XAML). The UI uses **code-behind** event handlers plus a lightweight **service locator** (`ServiceHelper` over the MAUI DI container) — not MVVM/data binding.
- **Cryptography** — `Sodium.Core` (libsodium): X25519 (ECDH), Ed25519 (signatures), plus .NET `AesGcm` (AES-256-GCM) and `HKDF`.
- **Session security** — per-session **forward secrecy**: the session root key is derived once with an X3DH-lite 3-DH handshake (identity key + signed prekey + one-time ephemeral key), then discarded. Each message consumes exactly one step of a direction-bound **message-level ratchet**; mildly out-of-order messages are handled through a bounded skipped-key cache.
- **Persistence** — `sqlite-net-pcl` (SQLite) with **SQLite3 Multiple Ciphers** (`SQLite3MC.PCLRaw.bundle`) for **encryption at rest**; a random 32-byte database key (DEK) is held in Android Keystore-backed storage. Identity **private keys** are likewise kept in **SecureStorage** (Android Keystore), never as plaintext on disk.
- **Bluetooth** — `Shiny.BluetoothLE` + `Shiny.BluetoothLE.Hosting` (advertising + scanning + GATT, both roles for peer-to-peer).
- **QR** — `ZXing.Net.Maui.Controls` (generate + scan).
- Architecture: a `SecureMesh.Core` library (crypto + domain, desktop-testable) referenced by the `SecureMesh.App` MAUI app and the `SecureMesh.Core.Tests` xUnit project.

## Prerequisites

This is an **Android-targeted** app. On Linux it builds the Android target; the evaluation host is **Windows 11** (Android emulator or device).

- .NET 10 SDK with the `maui-android` workload.
- **JDK 21** for Android builds. The .NET 10 Android workload requires JDK 21; JDK 26 fails with the Xamarin.Android error **XA0030** ("Building with JDK version … is not supported").
- Android SDK + two physical Android devices for real testing (the emulator has no Bluetooth radio).

## Build & test

```bash
# Build the Android app (JDK 21 required)
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk
dotnet build SecureMesh.App/SecureMesh.App.csproj -f net10.0-android -c Debug

# Run the crypto + messaging test suite (desktop, no hardware needed)
dotnet test SecureMesh.Core.Tests/SecureMesh.Core.Tests.csproj
```

The test suite (74 tests) covers key generation, X25519 agreement, AES-GCM tamper detection, Ed25519 signatures, the ratchet, message envelopes, forward-secrecy reliability, group-chat domain logic, chat history, and the full end-to-end encrypted message flow over mock transports.

## How to use (two phones)

1. Install the app on two phones (no internet needed).
2. On first launch each user picks a nickname → an identity (keypair) is generated and shown as a QR code.
3. **Add a contact** by scanning each other's QR; both phones show a matching **safety number** — compare it to confirm.
4. Open the chat and send encrypted text messages over Bluetooth.

## Known limitations

- **Group chat end-to-end over BLE is unverified.** Creating/listing groups, adding members, and sending/receiving group messages are implemented (the domain logic is unit-tested and the group UI is wired), but — like 1:1 chat — real group messaging over Bluetooth still needs two physical devices to exercise.
- **Session forward secrecy requires peers to be connected to establish a session.** The per-session root key is derived from a prekey handshake that needs both devices online and paired; offline/asynchronous prekey delivery is not implemented yet.
- **BLE runtime is unverified** — the app compiles and the logic is unit-tested, but real advertising/connection/messaging has not been exercised on two physical devices (pending physical hardware).
- **Range** is Bluetooth's: ~10–100 m. There is no relay through phones that don't run the app (physically impossible), and no cross-city delivery without the internet.
- Chat history is stored inside the encrypted-at-rest database and is re-loaded when a conversation reopens.
- No voice/video (out of scope by decision).

## Source attribution

- [Bridgefy](https://github.com/bridgefy) — design reference (three delivery modes, store-and-forward, dedup); also the cautionary tale (broken crypto, "Breaking Bridgefy" CT-RSA 2021 / USENIX Security 2022).
- [Briar](https://briarproject.org/) — the QR-code / in-person key-exchange identity model.
- [libsodium](https://doc.libsodium.org/) / Sodium.Core — cryptographic primitives.
- [Shiny.BluetoothLE](https://github.com/shinyorg/shiny) — MAUI Bluetooth transport.
- Research reports: see `docs/research/bt/` and the spec at `docs/project/SECUREMESH_SPEC.md`.
