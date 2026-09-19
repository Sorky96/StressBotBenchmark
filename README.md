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
- Matching account naming and password rules on the target server
- If API login is enabled: a working authentication API endpoint

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run -- 1000
# Or specify a custom config file:
dotnet run -- --config my_config.json
```

The first argument can be the number of bots to launch (overrides config).

## Configuration

Settings can be edited directly in [config.json](./config.json) without recompiling. If `config.json` is missing, the application will automatically generate one with default values.

Important settings:

- Host: `127.0.0.1`
- Port: `7172`
- Use API login: `false`
- API login URL: `http://127.0.0.1:5185/auth/login`
- Bot count: `1000`
- Account prefix: `stressbot`
- Password: `test123`
- Login delay: `15 ms`
- Burst size: `20`
- Burst pause: `300 ms`
- Spell support: enabled
- Attack support: enabled
- Random walking: disabled
- Chat: disabled
- Reconnect: enabled

## How It Works

1. The application creates bot names based on the configured prefix and numeric suffix.
2. If `UseApiLogin` is enabled, each bot performs an HTTP login request through `ApiLoginAsync`.
3. The bot opens a TCP connection to the game server (port 7172 by default).
4. The bot completes the protocol handshake, sends the login packet, and starts its read/write loops.
5. Optional behavior loops send movement, spell, chat, and attack packets.
6. A Spectre.Console dashboard displays real-time benchmark statistics.

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

## Adapting It To Other Engines

This project is not engine-agnostic yet. To make it work with another server engine, you will usually need to update both the login flow and the packet protocol.

### 1. Disable or replace API login

Right now, every bot calls `ApiLoginAsync` before opening the game connection. If your target engine does not use a web authentication layer, you should:

- remove the `ApiLoginUrl` dependency from [BotConfig.cs](./BotConfig.cs),
- skip the `ApiLoginAsync` call in [TibiaBot.cs](./TibiaBot.cs),
- connect directly to the game socket from `StartAsync`,
- optionally replace API login with a local pre-check or no pre-check at all.

Recommended minimal change:

1. Add a configuration flag such as `UseApiLogin`.
2. If `UseApiLogin` is `false`, bypass `ApiLoginAsync`.
3. Continue directly to `ConnectAndRunAsync`.

In practical terms, the logic in `StartAsync` should become conditional instead of always requiring HTTP authentication.

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
- disable HTTP API login without editing core bot lifecycle code every time.

## Notes

- The dashboard title still references `Tibia 10.98 StressBot Cluster`, which reflects the current protocol target.
- Command-line argument parsing is minimal for now and only overrides bot count.
- This repository currently focuses on benchmarking behavior, not clean abstraction across multiple engines.
