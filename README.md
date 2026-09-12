# BDVM - Full

**BDVM** stands for **Bunchy's Derail Valley Mods**, the common banner for this
modular Derail Valley project.

`BDVM.Full` is the current installable Unity Mod Manager composition of the BDVM project. It wires the modular economy, fleet, operations, web features and optional-in-architecture bridges into one Derail Valley entry point. Standalone module packages and the complete profile are published as beta prereleases.

## Status

| Property | Value |
| --- | --- |
| Module kind | Integration bundle |
| Manifest version | 0.3.0 |
| Target framework | .NET Framework 4.8 (`net48`) |
| Mod loader | Unity Mod Manager 0.27.3 or compatible |
| Current declared requirements | BDVM `Multiplayer` fork with `MultiplayerAPI` 1.4.0+, `SelfShunt`, `RemoteDispatchLive`; PassengerJobs is optional |
| Release state | Beta development candidate; the first stable release will be 1.0.0 |

## Included modules

The bundle references `BDVM.Common`, `BDVM.Core`, `BDVM.Companies`, `BDVM.Fleet`, `BDVM.Market`, `BDVM.Operations`, `BDVM.Passengers`, `BDVM.Web`, `BDVM.Dispatch`, `BDVM.Management`, `BDVM.MultiplayerBridge` and `BDVM.SelfShuntBridge`. `BDVM.PassengerJobsBridge` remains an optional integration loaded only when a compatible PassengerJobs runtime is present.

Feature domain and integration files remain owned by their module repositories. `BDVM.Full.csproj` links the sources that are not yet emitted by standalone module packages, preventing duplicate runtime types while preserving repository ownership.

## Responsibilities and current capabilities

The 0.3.0 composition binds `BDVM.Management` snapshots and intents to the authoritative runtime through a transport-independent adapter. Company, wallet, fleet, market, delivery, leasing, assignment, passenger, financing, yard and available industrial actions execute on the host and use the existing save boundary.

Starter rolling stock is delivered with the native comms radio. Select **BDVM DELIVERY**, use the radio A/B buttons to choose the next owned vehicle, aim at an empty depot or service track in the current station, and press Use. The default DE2 and three flatcars are delivered one vehicle at a time, and every successful placement is persisted before it can be repeated.

The beta validation interface can create one end-to-end starter freight job after the three delivered flatcars are placed together on a compatible warehouse track. It selects a real cargo and destination warehouse, uses the owned CarGUIDs in a persistent SelfShunt job chain, and relies on the game's normal booklet, loading machine, unloading machine and payment flow. It never creates replacement wagons.

The industrial interface discovers the loaded warehouse network supported by available freight wagons and derives live transport choices directly from persistent source stock and destination shortages. Nothing is published, accepted, expired or reserved: the operator chooses a quantity and exact owned/company wagons, and BDVM immediately creates a zero-wage SelfShunt movement. Observed physical loading removes source stock; observed unloading adds destination stock and settles a payment recalculated from current scarcity. Source-only and sink-only recipes keep the graph live, while route, fuel, consumables and wear estimates can make a poor movement unprofitable. Correlations survive save/reload, cancellation conserves in-transit cargo, and no rolling stock is created for transport work.

- Initialize host-authoritative player, company and wallet state from the loaded career.
- Persist BDVM checkpoints through the Derail Valley save hook.
- Manage company creation, membership, funds and economic diagnostics.
- Exercise audited acquisition and resale flows through Unity vehicle adapters.
- Protect economically owned assets from unsafe cleanup.
- Compose finite-market, licensing, financing, industrial, mission and passenger domain services.
- Project live station stock needs and expose direct host-validated quantity and personal/company wagon choices to Management without offer or reservation objects.
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
- The current `info.json` requires Multiplayer, SelfShunt and Remote Dispatch Live. PassengerJobs is an optional integration: when its API is unavailable, freight snapshots and management remain usable and passenger features fail closed.
- The web platform does not grant business authority to browser code; routes expose read models and authenticated intents only.
- No AI train drivers are included. Maintenance remains player-organized and manual.
- No compatibility facade or automatic import of unsupported prototype checkpoints is shipped.
- Do not assume an arbitrary build is save-compatible; use test saves until a release explicitly guarantees migration.

## Source dependencies

All 14 `BDVM.*` repositories must be siblings under `src/`. Building requires a Derail Valley installation for Unity, game and Unity Mod Manager assemblies. It also requires `MultiplayerAPI.dll` 1.4.0 or later plus compatible `SelfShunt.API.dll`; `PassengerJobs.API.dll` is needed only to compile the optional PassengerJobs bridge. `RemoteDispatchLive`, the BDVM Multiplayer fork and `SelfShunt` are runtime dependencies of the complete profile, while PassengerJobs and `DVLangHelper` are optional integrations. Multiplayer API 1.4 supplies the authenticated, durable, initialized-once individual wallets used by remote economic actions; an earlier API is not a compatible complete-profile runtime.

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
dotnet run --project .\tests\BDVM.Domain.Tests\BDVM.Domain.Tests.csproj -c Release
```

Validate against a disposable save until persistence compatibility is formally released. Tests should cover solo host startup, save/reload, UI input blocking, company money, asset acquisition/resale, generator suppression and multiplayer host/client refusal paths.

## Installation

Beta packages are published on GitHub. For a development install, prefer the coordinated `BDVM.Full` beta profile and keep the compatible Multiplayer, Remote Dispatch, SelfShunt and Passenger Jobs forks aligned with its preflight. Do not combine unmatched module DLLs from different release sets. No stable package is published before `1.0.0`.

## Manual beta release

The `Build beta release` GitHub Actions workflow is manual-only. It requires a
private self-hosted Windows runner labelled `bdvm-release` with Derail Valley
installed, because the game assemblies cannot be redistributed to GitHub-hosted
runners. The operator supplies a tag matching `v0.x.y-beta.n`; any stable or
post-1.0 tag is refused. The workflow checks out the complete module graph and
the four integration forks, builds the required APIs and bundle, records every
source commit in `provenance.json`, publishes SHA-256 checksums and always marks
the GitHub release as a prerelease.

The first stable suite release remains reserved for `1.0.0`.

## Upstream integrations and attribution

- SelfShunt: [original](https://github.com/Chump-the-Lump/DV-SelfShunter), [BDVM fork](https://github.com/Bunchyearth23/DV-SelfShunter), recorded base `329c85cf51715404af3b4d455239d9fc54f5ac5b`, credit `Chump_the_Lump`.
- Multiplayer: [AMacro upstream](https://github.com/AMacro/dv-multiplayer), [BDVM fork](https://github.com/Bunchyearth23/dv-multiplayer), Apache-2.0.
- Remote Dispatch: [mspielberg upstream](https://github.com/mspielberg/dv-remote-dispatch), [BDVM fork](https://github.com/Bunchyearth23/dv-remote-dispatch), MIT.
- Passenger Jobs: [katycat5e upstream](https://github.com/katycat5e/DVPassengerJobs), [BDVM fork](https://github.com/Bunchyearth23/DVPassengerJobs), recorded upstream audit revision `9bb668cbc2f3d270d282b2b3297667f01bec3e18`, MIT; the fork provides the versioned `PassengerJobs.API` consumed by `BDVM.PassengerJobsBridge`, and no upstream code is copied into the bridge.

Required upstream notices and attribution must remain present in redistributed builds.

## Compatibility

Use modules, forks and API assemblies from the same tested release set. Checkpoint schemas, protocol versions, route identifiers and persistent asset IDs are compatibility boundaries; incompatible inputs must be refused rather than guessed. Until a release matrix is published, development builds should be tested on disposable saves and are not guaranteed to support downgrade.

Strict population control is disabled by default. It requires the SaveGameData hook, an authoritative host, a new non-tutorial career or an existing save already carrying a BDVM checkpoint, and compatible SelfShunt and PassengerJobs generation controls. If any precondition fails, generator interception remains inactive and the refusal is logged; the runtime never applies a partial strict policy silently.

## License

BDVM code is licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and the applied copyright [NOTICE](NOTICE). Incorporated or separately distributed upstream work remains subject to its own license or recorded permission.
