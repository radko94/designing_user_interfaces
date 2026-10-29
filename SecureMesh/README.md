# SecureMesh — Encrypted Bluetooth Messenger

## Purpose and functionality

SecureMesh is an **offline-first, peer-to-peer messenger for Android** built with C# / .NET 10 / .NET MAUI. It lets two nearby phones exchange **end-to-end-encrypted text messages over Bluetooth with no internet and no central server**, using an in-person **QR-code** exchange to verify identities.

It is inspired by **Bridgefy** and **Briar**, but its differentiator is **real, vetted cryptography** — Bridgefy's own encryption was broken twice; SecureMesh uses standard, proven primitives.

**Current status (MVP):** 1:1 text chat is implemented and the core is fully unit/integration-tested. Group chat is modeled but not yet wired (see Limitations). Voice/video are intentionally out of scope.

## Technology stack

- C# on .NET 10 (`net10.0-android`), .NET MAUI (XAML), MVVM.
- **Cryptography** — `Sodium.Core` (libsodium): X25519 (ECDH), Ed25519 (signatures), plus .NET `AesGcm` (AES-256-GCM) and `HKDF`. A per-message key ratchet provides forward secrecy.
- **Persistence** — `sqlite-net-pcl` (SQLite): contacts, conversations, messages (encrypted at rest), sync state.
- **Bluetooth** — `Shiny.BluetoothLE` + `Shiny.BluetoothLE.Hosting` (advertising + scanning + GATT, both roles for peer-to-peer).
- **QR** — `ZXing.Net.Maui.Controls` (generate + scan).
- Architecture: a `SecureMesh.Core` library (crypto + domain, desktop-testable) referenced by the `SecureMesh.App` MAUI app and the `SecureMesh.Core.Tests` xUnit project.

## Prerequisites

This is an **Android-targeted** app. On Linux it builds the Android target; the evaluation host is **Windows 11** (Android emulator or device).

- .NET 10 SDK with the `maui-android` workload.
- **JDK 21** (the .NET 10 Android workload requires JDK 21; newer JDKs such as 26 are rejected).
- Android SDK + two physical Android devices for real testing (the emulator has no Bluetooth radio).

## Build & test

```bash
# Build the Android app (JDK 21 required)
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk
dotnet build SecureMesh.App/SecureMesh.App.csproj -f net10.0-android -c Debug

# Run the crypto + messaging test suite (desktop, no hardware needed)
dotnet test SecureMesh.Core.Tests/SecureMesh.Core.Tests.csproj
```

The test suite (37 tests) covers key generation, X25519 agreement, AES-GCM tamper detection, Ed25519 signatures, the ratchet, the message envelope, and the full end-to-end encrypted message flow over mock transports.

## How to use (two phones)

1. Install the app on two phones (no internet needed).
2. On first launch each user picks a nickname → an identity (keypair) is generated and shown as a QR code.
3. **Add a contact** by scanning each other's QR; both phones show a matching **safety number** — compare it to confirm.
4. Open the chat and send encrypted text messages over Bluetooth.

## Known limitations

- **1:1 chat only** (MVP). Group chat is modeled (tables + repositories) but not wired to the transport yet.
- **BLE runtime is unverified** — the app compiles and the logic is unit-tested, but real advertising/connection/messaging has not been exercised on two physical devices.
- **Range** is Bluetooth's: ~10–100 m. There is no relay through phones that don't run the app (physically impossible), and no cross-city delivery without the internet.
- Chat history is stored encrypted at rest; the current UI shows session bubbles only (no re-decrypt-history view yet).
- No voice/video (out of scope by decision).

## Source attribution

- [Bridgefy](https://github.com/bridgefy) — design reference (three delivery modes, store-and-forward, dedup); also the cautionary tale (broken crypto, "Breaking Bridgefy" CT-RSA 2021 / USENIX Security 2022).
- [Briar](https://briarproject.org/) — the QR-code / in-person key-exchange identity model.
- [libsodium](https://doc.libsodium.org/) / Sodium.Core — cryptographic primitives.
- [Shiny.BluetoothLE](https://github.com/shinyorg/shiny) — MAUI Bluetooth transport.
- Research reports: see `docs/research/bt/` and the spec at `docs/project/SECUREMESH_SPEC.md`.
