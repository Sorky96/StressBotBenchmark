# StressBotBenchmark

StressBotBenchmark is a .NET 8 console benchmark tool for spawning a large number of lightweight game bots, connecting them to a server, and tracking live load metrics in the terminal.

The current implementation is tailored to a Tibia-like protocol stack and is tightly coupled to:

- the Tibia 10.98 login/game protocol flow,
- RSA and XTEA packet handling,
- Tibia-specific opcodes for login, chat, spells, walking, attack, and ping responses,
- an optional HTTP login API (disabled by default) when connecting to servers that require web auth.

## Features

- Launches large batches of bots with configurable burst pacing
- Displays a live terminal dashboard with network and action metrics
- Supports optional random walking, spell casting, chat, and attacking
- Tracks reconnects, disconnects, bandwidth, packets, and basic combat activity
- Uses asynchronous socket handling for high bot counts

## Requirements

- .NET 8 SDK
- A compatible game server
- Bot accounts and characters on the target server matching the configured prefix, numbering and password (see [Creating Bot Accounts](#creating-bot-accounts))
- If API login is enabled: a working authentication API endpoint

The .NET SDK is only needed to build from source; the prebuilt [releases](#releases) are self-contained.

## Build

```bash
dotnet build
```

## Releases

Ready-to-run, self-contained builds (no .NET runtime needed) are published on the [Releases](../../releases) page for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64`. Each archive contains the executable, `config.json`, `README.md` and `seed_accounts.sql`, and the release includes a `SHA256SUMS.txt` file.

Releases are built by [.github/workflows/release.yml](./.github/workflows/release.yml) entirely on GitHub - no local tagging needed:

1. Merge your changes into `master`.
2. Go to **Actions -> Release -> Run workflow** (branch `master`).
3. Pick a version bump (`patch` / `minor` / `major`) or enter an explicit version (e.g. `1.0.0`), optionally tick **pre-release**.

The workflow calculates the next version from the latest `vX.Y.Z` tag (e.g. `v1.2.3` + `minor` = `v1.3.0`; with no tags yet, `major` gives `v1.0.0`), builds and packages all platforms, and only after every build succeeds it creates the tag on `master` and publishes the release with notes. Versions with a suffix (e.g. `1.1.0-beta.1`) are marked as pre-releases.

Pushing a `v*` tag manually (`git push origin v1.0.0`) also triggers a release.

## Creating Bot Accounts

[seed_accounts.sql](./seed_accounts.sql) creates the bot accounts and one character per account (with the same name) in a TFS-style MySQL/MariaDB schema (`accounts` / `players` tables, SHA1 passwords). Run it in phpMyAdmin or any MySQL client against the game database.

Before running it, make the variables at the top of the script match your `config.json`:

| SQL variable | Must match |
| --- | --- |
| `@bot_count` | at least `BotCount` (default in the script: `20000`) |
| `@prefix` | `Prefix` + `_` (e.g. `stressbot_`) |
| `@password` | `Password` |
| `@min_width` | `AccountWidth` |

The script uses `INSERT IGNORE`, so it is safe to re-run. Characters are created as level 50 sorcerers (`vocation = 1`) with large mana pools so they can keep casting the default spell; adjust the stats or vocation if your server differs.

## Run

```bash
dotnet run -- 1000
# Or specify a custom config file (and optionally override the bot count):
dotnet run -- --config my_config.json --bots 500
```

With a release build, run the executable directly instead, e.g. `./StressBotBenchmark --bots 500` (or `StressBotBenchmark.exe --bots 500` on Windows).

Arguments:

- `N` or `--bots N` / `--bots=N` - number of bots to launch (overrides `BotCount` from the config); a positional `N` can be combined with `--config`,
- `--config path` / `-c path` / `--config=path` - config file to load. The file must exist.

A missing or invalid config file, an invalid bot count, or an unknown argument stops the program with exit code 2 and prints the usage line. At startup the tool prints which config file was loaded and whether HTTP API login is enabled.

## Configuration

Settings can be edited directly in [config.json](./config.json) without recompiling. Without `--config`, `config.json` is looked up in the working directory and then next to the executable. If it is not found in either place, the application generates one with default values in the working directory. A config file that cannot be parsed stops the program instead of silently falling back to defaults.

Keys are case-insensitive; comments and trailing commas are allowed.

| Key | Default | Description |
| --- | --- | --- |
| `Host` | `127.0.0.1` | Game server address |
| `Port` | `7172` | Game server port |
| `UseApiLogin` | `false` | Call the HTTP login API before connecting |
| `ApiLoginUrl` | `http://127.0.0.1:5185/auth/login` | HTTP login endpoint (used only when `UseApiLogin` is `true`) |
| `BotCount` | `1000` | Number of bots to launch (overridden by the command line) |
| `Prefix` | `stressbot` | Account/character name prefix |
| `Password` | `test123` | Password shared by all bot accounts |
| `AccountWidth` | `3` | Minimum zero-padded width of the bot number (`stressbot_001`) |
| `LoginDelayMs` | `15` | Delay between launching consecutive bots |
| `BurstSize` | `20` | Bots launched before a burst pause |
| `BurstPauseMs` | `300` | Pause after each burst |
| `EnableRandomWalk` / `WalkIntervalMs` | `true` / `500` | Random walking and its interval |
| `EnableSpell` / `SpellIntervalMs` / `SpellText` | `true` / `5000` / `exevo gran mas flam` | Spell casting |
| `EnableChat` / `ChatIntervalMs` | `false` / `5000` | Chat messages |
| `EnableAttack` / `AttackScanIntervalMs` | `true` / `800` | Attacking nearby monsters and how often to scan for targets |
| `EnableChaseMode` | `true` | Chase the target (also pauses random walking while attacking) |
| `FightMode` | `1` | Fight mode byte sent to the server (`1` = offensive) |
| `SafeFight` | `false` | Secure/safe fight mode flag |
| `Reconnect` | `true` | Reconnect bots after a disconnect |
| `DashboardIntervalMs` | `1000` | Dashboard refresh interval |
| `LoginOnly`, `QueueSize`, `MaxSendLagMsToDrop` | `false`, `32`, `1200` | Present in the config but not used by the current implementation |

## How It Works

1. The application loads the config, applies command-line overrides, and creates bot names as `{Prefix}_{number}` zero-padded to `AccountWidth` digits.
2. If `UseApiLogin` is enabled, each bot performs an HTTP login request through `ApiLoginAsync`.
3. The bot opens a TCP connection to the game server (port 7172 by default).
4. The bot completes the protocol handshake, sends the login packet, and starts its read/write loops.
5. Bots are launched in bursts (`BurstSize` bots, `LoginDelayMs` apart, then `BurstPauseMs`) to avoid flooding the server with simultaneous logins.
6. Optional behavior loops send movement, spell, chat, and attack packets.
7. A Spectre.Console dashboard displays real-time benchmark statistics.

## Project Structure

- [config.json](./config.json) - benchmark runtime configuration
- [Program.cs](./Program.cs) - startup, bot launching, live dashboard
- [BotConfig.cs](./BotConfig.cs) - configuration model and JSON loader
- [TibiaBot.cs](./TibiaBot.cs) - per-bot connection lifecycle and behavior
- [BotMetrics.cs](./BotMetrics.cs) - shared counters and performance telemetry
- [Network/InputMessage.cs](./Network/InputMessage.cs) - packet reading helpers
- [Network/OutputMessage.cs](./Network/OutputMessage.cs) - packet writing helpers
- [Network/Rsa.cs](./Network/Rsa.cs) - RSA encryption support
- [Network/Xtea.cs](./Network/Xtea.cs) - XTEA encryption support
- [seed_accounts.sql](./seed_accounts.sql) - SQL script creating bot accounts and characters
- [.github/workflows/release.yml](./.github/workflows/release.yml) - release build and publishing workflow

## Adapting It To Other Engines

This project is not engine-agnostic yet. To make it work with another server engine, you will usually need to update both the login flow and the packet protocol.

### 1. Configure or replace API login

HTTP API login is already optional: with `UseApiLogin` set to `false` (the default), `StartAsync` in [TibiaBot.cs](./TibiaBot.cs) connects directly to the game socket. If your engine uses a web authentication layer, set `UseApiLogin` to `true` and point `ApiLoginUrl` at it. `ApiLoginAsync` posts `{"emailOrUsername": ..., "password": ...}` as JSON; if your endpoint expects a different request or returns a token that must be passed to the game login, adapt `ApiLoginAsync` accordingly.

### 2. Replace protocol-specific login packet logic

The current `SendLoginMessageAsync` implementation is specific to the existing protocol. For another engine, verify and update:

- client version / protocol version,
- login packet layout,
- RSA payload format,
- timestamp or challenge fields,
- account and password serialization,
- checksum rules.

### 3. Replace encryption or remove it if unsupported

This benchmark currently uses:

- RSA during login,
- XTEA for game traffic,
- Adler-32 checksums for outgoing packets.

If another engine uses different encryption, token-based auth, or plain packets, update:

- [Network/Rsa.cs](./Network/Rsa.cs),
- [Network/Xtea.cs](./Network/Xtea.cs),
- the packet wrapping logic in [TibiaBot.cs](./TibiaBot.cs).

### 4. Update opcode handling

Walking, talking, spell casting, attack, and ping handling are all based on hardcoded opcodes. For another engine, adjust:

- login response parsing,
- ping / keepalive opcodes,
- movement opcodes,
- chat or command opcodes,
- attack and combat mode opcodes.

This is handled mainly in [TibiaBot.cs](./TibiaBot.cs).

### 5. Replace world-state heuristics

The monster tracking and damage detection logic currently relies on Tibia-specific byte patterns and heuristics. Another engine may require:

- structured packet parsing instead of heuristic scanning,
- different creature ID ranges,
- different damage text or event packets,
- different combat state detection.

### 6. Review account generation assumptions

The bot naming scheme assumes generated accounts such as `stressbot_001`, `stressbot_002`, and so on. For other engines, you may need to change:

- account name format,
- character name rules,
- password rules,
- whether account login and character login are separate steps.

## Suggested First Refactor For Multi-Engine Support

If you want this project to support multiple engines cleanly, the best next step is to split `TibiaBot` into interchangeable parts:

- `ILoginStrategy`
- `IProtocolAdapter`
- `IBehaviorAdapter`

That would let you:

- keep one benchmark runner,
- plug in different login flows,
- switch packet formats per engine,
- swap authentication flows without editing core bot lifecycle code.

## Notes

- The dashboard title still references `Tibia 10.98 StressBot Cluster`, which reflects the current protocol target.
- This repository currently focuses on benchmarking behavior, not clean abstraction across multiple engines.
