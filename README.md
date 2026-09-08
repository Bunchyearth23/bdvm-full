# BDVM - Full

`BDVM.Full` is the current installable Unity Mod Manager composition of the BDVM project. It wires the modular economy, fleet, operations, web features and optional-in-architecture bridges into one Derail Valley entry point while standalone module packages are still being prepared.

## Status

| Property | Value |
| --- | --- |
| Module kind | Integration bundle |
| Manifest version | 2.2.0 |
| Target framework | .NET Framework 4.8 (`net48`) |
| Mod loader | Unity Mod Manager 0.27.3 or compatible |
| Current declared requirements | `Multiplayer`, `SelfShunt`, `PassengerJobs`, `RemoteDispatchLive` |
| Release state | Development build; packaging and compatibility matrix are not final |

## Included modules

The bundle references `BDVM.Common`, `BDVM.Core`, `BDVM.Companies`, `BDVM.Fleet`, `BDVM.Market`, `BDVM.Operations`, `BDVM.Passengers`, `BDVM.PassengerJobsBridge`, `BDVM.Web`, `BDVM.Dispatch`, `BDVM.Management`, `BDVM.MultiplayerBridge` and `BDVM.SelfShuntBridge`.

Feature domain and integration files remain owned by their module repositories. `BDVM.Full.csproj` links the sources that are not yet emitted by standalone module packages, preventing duplicate runtime types while preserving repository ownership.

## Responsibilities and current capabilities

- Initialize host-authoritative player, company and wallet state from the loaded career.
- Persist BDVM checkpoints through the Derail Valley save hook.
- Manage company creation, membership, funds and economic diagnostics.
- Exercise audited acquisition and resale flows through Unity vehicle adapters.
- Protect economically owned assets from unsafe cleanup.
- Compose finite-market, licensing, financing, industrial, mission and passenger domain services.
- Validate that a compatible Passenger Jobs runtime is present before accepting a passenger mission ID.
- Register Dispatch and Management web modules.
- Adapt the current browser transport through the BDVM Remote Dispatch fork.
- Adapt host-authoritative multiplayer protocol through the BDVM Multiplayer fork.
- Coordinate competing generation through the authorized SelfShunt fork.
- Optionally enforce the strict rolling-stock population policy at targeted vanilla, Multiplayer, SelfShunt and PassengerJobs generator boundaries.
- Show the in-game management window with `F7`; the UI is intended for mouse mode and blocks world interaction while open.

## Boundaries and known limitations

- This is a development composition, not the final per-module distribution.
- A dedicated server is planned for later; this build remains a solo/host runtime.
- The current `info.json` requires Multiplayer, SelfShunt, Passenger Jobs and Remote Dispatch Live because their bridges are included. The architecture permits omitting bridges later, but this bundle does not yet package that choice.
- The web platform does not grant business authority to browser code; routes expose read models and authenticated intents only.
- No AI train drivers are included. Maintenance remains player-organized and manual.
- No compatibility facade or automatic import of old `DVCompany` checkpoints is shipped.
- Do not assume an arbitrary build is save-compatible; use test saves until a release explicitly guarantees migration.

## Source dependencies

All 14 `BDVM.*` repositories must be siblings under `src/`. Building requires a Derail Valley installation for Unity, game and Unity Mod Manager assemblies. It also requires a compatible `MultiplayerAPI.dll` and the authorized SelfShunt fork/API at the paths expected by the project. Passenger Jobs and Remote Dispatch Live are runtime dependencies, not linked build assemblies; Passenger Jobs itself requires DVLangHelper.

## Build

From the integration workspace:

```powershell
dotnet build .\BDVM.slnx -c Release -p:GameDir="D:\Steam\steamapps\common\Derail Valley"
```

Or build only the bundle:

```powershell
dotnet build .\src\BDVM.Full\BDVM.Full.csproj -c Release -p:GameDir="D:\Steam\steamapps\common\Derail Valley"
```

Adjust `GameDir` for the local installation. A successful compile does not create a final release archive; packaging remains a separate milestone.

## Testing and validation

The integration workspace provides:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-W039BdvmMigration.ps1
dotnet run --project .\tests\DVCompany.DomainTests\DVCompany.DomainTests.csproj -c Release
```

Validate against a disposable save until persistence compatibility is formally released. Tests should cover solo host startup, save/reload, UI input blocking, company money, asset acquisition/resale, generator suppression and multiplayer host/client refusal paths.

## Installation

No final package is published yet. For a development install, use the output prepared by the workspace packaging process and keep the compatible Multiplayer, Remote Dispatch and SelfShunt forks aligned with the tested revisions. Do not combine unmatched module DLLs from different commits.

## Upstream integrations and attribution

- SelfShunt: [original](https://github.com/Chump-the-Lump/DV-SelfShunter), [BDVM fork](https://github.com/Bunchyearth23/DV-SelfShunter), recorded base `329c85cf51715404af3b4d455239d9fc54f5ac5b`, credit `Chump_the_Lump`.
- Multiplayer: [AMacro upstream](https://github.com/AMacro/dv-multiplayer), [BDVM fork](https://github.com/Bunchyearth23/dv-multiplayer), Apache-2.0.
- Remote Dispatch: [mspielberg upstream](https://github.com/mspielberg/dv-remote-dispatch), [BDVM fork](https://github.com/Bunchyearth23/dv-remote-dispatch), MIT.
- Passenger Jobs: [katycat5e/DVPassengerJobs](https://github.com/katycat5e/DVPassengerJobs), recorded audit revision `9bb668cbc2f3d270d282b2b3297667f01bec3e18`, MIT; no upstream code is copied into the bridge.

Required upstream notices and attribution must remain present in redistributed builds.

## Compatibility

Use modules, forks and API assemblies from the same tested release set. Checkpoint schemas, protocol versions, route identifiers and persistent asset IDs are compatibility boundaries; incompatible inputs must be refused rather than guessed. Until a release matrix is published, development builds should be tested on disposable saves and are not guaranteed to support downgrade.

Strict population control is disabled by default. It requires the SaveGameData hook, an authoritative host, a new non-tutorial career or an existing save already carrying a BDVM checkpoint, and compatible SelfShunt and PassengerJobs generation controls. If any precondition fails, generator interception remains inactive and the refusal is logged; the runtime never applies a partial strict policy silently.

## License

BDVM code is licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and the applied copyright [NOTICE](NOTICE). Incorporated or separately distributed upstream work remains subject to its own license or recorded permission.
