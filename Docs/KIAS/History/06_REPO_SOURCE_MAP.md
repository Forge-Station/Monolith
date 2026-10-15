# Current source map (checked on `KIAS`, `e32aecc197f...`)

## New TODO → source

| Task | Files, relevant confirmed paths |
| --- | --- |
| T01 ID / T02 room | `Content.Server/_Forge/KIAS/KiasDeviceIdentitySystem.cs`, `Content.Shared/_Forge/KIAS/KiasComponents.cs`, `Content.Server/_Forge/KIAS/Controllers/KiasControllerUiSystem.cs`, `Content.Shared/_Forge/KIAS/Controllers/KiasControllerProgram.cs`, `Content.Client/_Forge/KIAS/Controllers/KiasControllerWindow.cs`, `KiasControllerLabels.cs` |
| T03/T04 palette/labels | `Content.Client/_Forge/KIAS/Controllers/KiasControllerWindow.cs`, `KiasGraphCanvas.cs`, `Content.Client/_Forge/KIAS/KiasUi.cs`, `Resources/Locale/ru-RU/_Forge/kias.ftl`, `Resources/Locale/en-US/_Forge/kias.ftl` |
| T05 Kit | `Resources/Prototypes/_Forge/KIAS/devices.yml` (`id: KiasIntegrationKit`); `Resources/Textures/_Forge/KIAS/Pack/integration-kit.rsi` |
| T06 Core | `Resources/Prototypes/_Forge/KIAS/kias.yml` (`id: KiasCore`, `parent: BaseKiasServer`, RSI/offset), `Resources/Textures/_Forge/KIAS/KiasCore.rsi`, `Docs/KIAS/CORE_SPRITE.md`, `KiasSpriteTests.cs` |
| T07 Relay | `Content.Server/_Forge/KIAS/KiasRelaySystem.cs`, `Content.Shared/_Forge/KIAS/KiasRelayComponent.cs`, `Resources/Prototypes/_Forge/KIAS/kias.yml`, `Resources/Locale/*/_Forge/kias.ftl`, `KiasRelayTests.cs` |
| T08 Scanner | `Content.Server/_Forge/KIAS/KiasCrewSystem.cs` (`RebuildCoverage`), `Content.Server/_Forge/KIAS/KiasIntegrationSystem.cs` (`ConnectRoom`, `Integrate`, `CanControl`), `Content.Server/_Forge/KIAS/KiasServiceSystem.cs`, `Content.Shared/_Forge/KIAS/KiasCrewComponents.cs`, `KiasDeviceConfigurationTests.cs`, `KiasUiStabilityTests.cs` |
| T09 button/speaker | `Content.Server/_Forge/KIAS/KiasDeviceSystem.cs`, `Content.Server/_Forge/KIAS/Controllers/KiasControllerIoSystem.cs` & `KiasControllerIoSystem.Commands.cs`, `Content.Server/_Forge/KIAS/KiasDisplaySystem.Audio.cs`, `Resources/Prototypes/_Forge/KIAS/devices.yml` (`id: KiasEmergencyButton`), `kias.yml` (`id: KiasSpeaker`), `controller_profiles.yml`, source/sink port prototypes/defaultLinks, `KiasSpeakerTests.cs` |
| T10 suppression | `Content.Server/_Forge/KIAS/KiasActuatorSystem.cs` (`Suppress()`), `Content.Server/_Forge/KIAS/KiasProtocolSystem.cs` (`OnFireAlarm`), `Content.Server/_Forge/KIAS/KiasSafetySystem.cs`, `Controllers/KiasControllerIoSystem.Commands.cs`, `Resources/Prototypes/_Forge/KIAS/kias.yml` (`KiasSuppression`, `KiasSuppress`, `KiasSuppressionCartridge`), `controller_presets.yml`, `KiasActuatorTests.cs` and `KiasProtocolTests.cs` |

## Preexisting architecture and key tests

- `Content.Server/_Forge/KIAS/KiasSystem.cs` — topology, online/device list, manager power state.
- `Content.Server/_Forge/KIAS/KiasRelaySystem.cs` — `_open` indexed by `(Grid,Tile,Channel)`, `QueueReflood` of nearby node containers.
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.cs`, `Content.Shared/_Forge/KIAS/Controllers/{KiasGraphMachine,KiasGraphCompiler,KiasGraphCatalog}.cs` — graph execution/strict port types.
- `Content.Server/DeviceLinking/Systems/DeviceLinkSystem.cs`, `Content.Server/DeviceNetwork/Systems/{NetworkConfiguratorSystem,DeviceListSystem}.cs` — core gameplay integration (do not patch speculatively).
- `Content.Server/Atmos/Monitor/Systems/{AirAlarmSystem,AtmosAlarmableSystem}.cs` — real danger chain.
- `Content.IntegrationTests/Tests/_Forge/KIAS/` contains 30+ existing suites; locate closest suite, do not invent new test runner.
- `Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml` — in-repo visual hierarchy reference.
- `Docs/KIAS/VALIDATION.md`, `Docs/KIAS/UI_STABILITY.md`, `Docs/KIAS/DEVICE_IDENTIFICATION.md` — historical verification; report current results separately.

## Exact Windows batch files — previously missed by agent

```text
Monolith\Scripts\bat\buildAllRelease.bat
Monolith\Scripts\bat\runQuickAll.bat
```

`buildAllRelease.bat` content on inspected commit:

```bat
@echo off
cd ../../
call git submodule update --init --recursive
call dotnet build -c Release
pause
```

`runQuickAll.bat` launches sibling `runQuickServer.bat` and `runQuickClient.bat` with `start`. **Run these scripts with CWD `Scripts\bat`**, and handle `pause` in the release script. Check success of spawned processes, not simply exit of launcher.

**Agent:** recheck all paths and historical assumptions on your actual working tree, do not treat this map as a substitute for code.
