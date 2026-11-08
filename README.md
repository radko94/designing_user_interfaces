# Designing User Interfaces — C# / .NET MAUI (CS431)

A collection of cross-platform mobile apps built with **C# and .NET MAUI for Android**, produced for the university course **"Design of User Interface" / „Дизайн на потребителски интерфейс" (CS431)** at **Burgas Free University**, taught by **Assoc. Prof. Dr. Dimitar Minchev**.

> **At a glance:** 1 course project + 19 exercise apps · C# · .NET 10 · .NET MAUI · Android

This repository holds two things:

1. **SecureMesh** — the course project: an end-to-end encrypted, offline **Bluetooth messenger**.
2. **19 small exercise apps** — modern re-implementations of the lecturer's cross-platform teaching examples.

---

## What is this, in plain words?

If you don't write C#, this section is for you. If you do, skip to [Repository layout](#repository-layout).

### 🕵️ SecureMesh — the course project

A phone app that lets **two people standing near each other send private text messages directly between their phones over Bluetooth** — no internet, no server, no account.

- Each person **scans the other's QR code** to confirm who they're really talking to.
- Every message is **encrypted end-to-end**, so only the two phones can read it.
- Messages are also **encrypted on the phone itself**, and the history is stored locally.
- It was built as a reaction to real-world failures: popular "offline messenger" apps claimed to be secure while shipping broken cryptography. SecureMesh instead uses only standard, publicly reviewed crypto building blocks.

See [SecureMesh/README.md](SecureMesh/README.md) for the full story and screenshots.

### 🧩 The exercise apps

Nineteen small teaching apps, each doing one simple thing — adding two numbers, generating the Fibonacci sequence, finding prime numbers, keeping a phone book, a click-counter game, downloading a web page, reading an RSS feed, reading JSON data.

Each exists in two numbered "versions" because the course demonstrates how the *same* app is built on different Microsoft frameworks over the years (**UWP → Xamarin.Forms → WinUI → .NET MAUI**). In this repository they are all rebuilt in the modern one — **.NET MAUI for Android**.

---

## Repository layout

Every folder is a self-contained app with its own `README.md` (purpose, build steps, and test cases). Chapter numbers refer to the course book.

### Course project

| Folder | What it is |
|---|---|
| [SecureMesh](SecureMesh/) | Encrypted, offline Bluetooth messenger — `Core` (crypto + logic), `App` (MAUI Android UI), `Core.Tests` (74 tests). |

### Chapter 1 — originally UWP (9 apps)

| Folder | What it does |
|---|---|
| [Suma1_0](Suma1_0/) | Adds two numbers |
| [Fibonacci1_0](Fibonacci1_0/) | Generates the Fibonacci sequence |
| [Primes1_0](Primes1_0/) | Prime-number checks |
| [Phonebook1_0](Phonebook1_0/) | A simple contacts / phone book |
| [ClickerMania1_1](ClickerMania1_1/) | Tap-counting game with a persistent counter |
| [ClickerMania1_2](ClickerMania1_2/) | Timed click game (clicks per minute) |
| [HtmlDownloader1_0](HtmlDownloader1_0/) | Downloads a web page and shows its text |
| [RssReader1_0](RssReader1_0/) | Reads an RSS feed |
| [JsonReader1_0](JsonReader1_0/) | Reads and displays JSON data |

### Chapter 2 — originally Xamarin.Forms (8 apps)

| Folder | What it does |
|---|---|
| [Suma2_0](Suma2_0/) | Adds two numbers |
| [Fibonacci2_0](Fibonacci2_0/) | Generates the Fibonacci sequence |
| [Primes2_0](Primes2_0/) | Prime-number checks |
| [Phonebook2_0](Phonebook2_0/) | A simple contacts / phone book |
| [ClickerMania2_0](ClickerMania2_0/) | Click-counter game |
| [HtmlDownloader2_0](HtmlDownloader2_0/) | Downloads a web page and shows its text |
| [RssReader2_0](RssReader2_0/) | Reads an RSS feed |
| [JsonReader2_0](JsonReader2_0/) | Reads and displays JSON data |

### Chapter 3 — originally WinUI (1 app)

| Folder | What it does |
|---|---|
| [Suma3_0](Suma3_0/) | Adds two numbers |

### Chapter 4 — originally .NET MAUI (1 app)

| Folder | What it does |
|---|---|
| [Suma4_0](Suma4_0/) | Adds two numbers |

---

## Technology stack

- **C#** on **.NET 10** (`net10.0-android`)
- **.NET MAUI** with **XAML** for the user interface
- **Android** as the target platform (runs in an emulator or on a device)
- The SecureMesh project additionally uses **libsodium** (cryptography), **SQLCipher** (encrypted local storage), **Shiny.BluetoothLE** (Bluetooth), and **ZXing.Net.Maui** (QR codes).

---

## Getting started (for developers)

Each folder is a standalone project with its own `README.md` containing exact prerequisites (Windows 11, .NET 10 SDK with the `maui-android` workload, JDK 21, Android SDK) and full build/run steps.

As a quick example:

```bash
# Build one exercise app (Android target)
dotnet build Suma1_0/Suma1_0.csproj -f net10.0-android -c Debug

# Run the course project's test suite (desktop, no Android device needed)
dotnet test SecureMesh/SecureMesh.Core.Tests/SecureMesh.Core.Tests.csproj
```

If a folder's README mentions installing an APK, that APK must first be built and signed — none is shipped by default.

---

## Course context & sources

This work follows the course book **"Developing Cross-Platform Apps"**:

- 📖 Course book (GitBook): <https://dimitar-minchev.gitbook.io/developing-cross-platform-apps/>
- 💻 Course examples (DCPA): <https://github.com/dimitarminchev/DCPA>
- Microsoft Learn: [.NET MAUI](https://learn.microsoft.com/dotnet/maui/) · [C#](https://learn.microsoft.com/dotnet/csharp/)

---

## License & attribution

- The **course book and its examples** are published under **CC-BY-NC-SA** — see the book's license page for terms.
- The exercise apps here are **educational adaptations** of those examples; the **SecureMesh** project is original coursework.
- A specific open-source license for this repository has **not been chosen yet**; if you want to reuse the code, please open an issue to ask.
