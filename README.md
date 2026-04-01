# Rust+ FCM command line tool (Unofficial)

A .NET command-line tool for receiving [Rust+](https://rust.facepunch.com/companion) push notifications via Firebase Cloud Messaging (FCM). Register your device with FCM, link your Steam account, and listen for real-time Rust+ notifications — server pairing, smart alarms, entity pairing, team logins, and more.

This project is C# code converted from NodeJS code from Rustplus.js CLI tool (https://github.com/liamcottle/rustplus.js)

## How It Works

```
┌──────────────┐     FCM Register       ┌──────────────────┐
│  CLI Tool    │ ──────────────────────►│  Google FCM      │
│              │     Expo Token         │  Expo Push       │
│  fcm-register│ ──────────────────────►│  Rust+ API       │
│              │     Steam Link         │                  │
│              │ ──────────────────────►│  Steam/Facepunch │
└──────────────┘                        └──────────────────┘

┌──────────────┐     TLS Connection     ┌──────────────────┐
│  CLI Tool    │ ◄─────────────────────►│  Google MCS      │
│              │     Push Notifications │  (mtalk.google)  │
│  fcm-listen  │ ◄──────────────────────│                  │
└──────────────┘                        └──────────────────┘
```

The project has two components:

### `RustPlus_FCM` — Class Library

Core library that handles all communication with Google and Rust+ services:

- **`GoogleFcm`** — Registers with Firebase Cloud Messaging by performing a Firebase Installation request, GCM checkin, and GCM token registration. Stores the Rust+ companion app credentials (API key, sender ID, package name, etc.) as constants.
- **`McsClient`** — Connects to Google's Mobile Connection Server (`mtalk.google.com:5228`) over TLS using the MCS protobuf protocol. Receives push notifications in real-time, responds to heartbeat pings, and emits parsed notification data via an event. Includes automatic reconnection with a configurable inactivity timeout (default: 1 hour).
- **`ApiClient`** — Fetches an Expo push token and registers it with the Rust+ Companion API.
- **`SteamPairing`** — Launches Google Chrome with a local callback server to link your Steam account with Rust+ via the Facepunch companion login page.
- **`ConfigManager`** — Reads and writes the JSON config file that stores FCM credentials, Expo push token, and Rust+ auth token.
- **`RustPlusNotification`** — Models for deserializing FCM notification payloads, including the nested JSON body with server/entity/alarm details.
- **`Protobuf`** — Lightweight protobuf reader and writer for encoding/decoding MCS protocol messages without code generation.
- **`FileLoggerProvider`** — Simple `ILoggerProvider` implementation that writes log entries to a text file.

### `RustPlus_FCM.Console` — Console Application

CLI entry point with two commands:

- **`fcm-register`** — One-time setup that registers with FCM, obtains an Expo push token, links your Steam account via Chrome, registers with the Rust+ Companion API, and saves all credentials to a config file.
- **`fcm-listen`** — Loads saved credentials and connects to Google's MCS server to listen for Rust+ push notifications. Displays parsed notification details (server pairing, alarms, entity pairing, team logins) and automatically reconnects on connection loss or inactivity.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Google Chrome (required for the `fcm-register` Steam linking step)

## Getting Started

### 1. Build

```bash
dotnet build
```

### 2. Register

Run the registration command to set up FCM credentials and link your Steam account:

```bash
dotnet run --project RustPlus_FCM.Console -- fcm-register
```

This will:
1. Register a device with Google FCM
2. Obtain an Expo push token
3. Open Chrome for you to log in with your Steam account
4. Register with the Rust+ Companion API
5. Save all credentials to `rustplus.config.json`

### 3. Listen for Notifications

```bash
dotnet run --project RustPlus_FCM.Console -- fcm-listen
```

The listener connects to Google's MCS server and displays incoming Rust+ notifications:

- **Server Pairing** — Server name, IP, port, player ID, and player token
- **Entity Pairing** — Smart switches, smart alarms, storage monitors, etc.
- **Alarms** — Smart alarm alerts (e.g. "Your base is under attack!")
- **Team Logins** — Teammate online/offline notifications

Press `Ctrl+C` to gracefully shut down.

### Options

```
Usage: rustplus <options> <command>

Commands:
  help            Print the usage guide.
  fcm-register    Register with FCM, Expo, and link your Steam account.
  fcm-listen      Listen for Rust+ notifications via FCM.

Options:
  --config-file <file>    Path to config file. (default: rustplus.config.json)
```

## Configuration

All credentials are stored in `rustplus.config.json` (created by `fcm-register`):

```json
{
  "fcm_credentials": {
    "gcm": {
      "androidId": "...",
      "securityToken": "...",
      "token": "..."
    },
    "fcm": {
      "token": "..."
    }
  },
  "expo_push_token": "ExponentPushToken[...]",
  "rustplus_auth_token": "..."
}
```

## Logging

The application uses `Microsoft.Extensions.Logging` with two outputs:

| Output | Minimum Level | Purpose |
|--------|--------------|---------|
| Console | Information | User-facing messages — connection status, notifications, errors |
| File (`rustplus.log`) | Debug | Full diagnostic log — MCS protocol details, heartbeats, raw data |

The log file is created next to the executable.

## Reconnection

The MCS client automatically reconnects when:

- No messages (including heartbeats) are received within the **inactivity timeout** (default: 1 hour)
- The server closes the connection
- A network error occurs

Reconnect attempts are spaced by a configurable delay (default: 5 seconds). Duplicate notifications (same body ID received consecutively) are automatically filtered out.

## Project Structure

```
├── RustPlus_FCM/                    # Class library
│   ├── ApiClient.cs                 # Expo + Rust+ API client
│   ├── ConfigManager.cs             # JSON config read/write
│   ├── FileLoggerProvider.cs        # File logging provider
│   ├── GoogleFcm.cs                 # FCM/GCM registration
│   ├── McsClient.cs                 # MCS protocol client with reconnect
│   ├── Protobuf.cs                  # Lightweight protobuf reader/writer
│   ├── RustPlusNotification.cs      # Notification models
│   └── SteamPairing.cs             # Steam account linking via Chrome
│
├── RustPlus_FCM.Console/            # Console application
│   └── Program.cs                   # CLI entry point
│
└── README.md
```

## License

This project is licensed under the [MIT License](LICENSE) — you are free to use, modify, and distribute the code, but you must include the original copyright notice and a reference to this repository as the source.

This project is not affiliated with Facepunch Studios. Rust and Rust+ are trademarks of Facepunch Studios.