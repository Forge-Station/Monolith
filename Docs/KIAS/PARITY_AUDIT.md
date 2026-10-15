# KIAS: regression audit, 2026-10-08

Baseline: `3e067d069ebb45b192db06db8a38354f9f053cca`, branch `KIAS`.

| Area | Result | Evidence |
|---|---|---|
| Controller item | WRITE updates physical name/description; drafts leave the item alone; metadata restored on load | `KiasControllerEditorTests`, `KiasPersistenceTests`; native client Rename → WRITE → Eject |
| Speaker graph | Each selected speaker receives real local speech; cooldown remains effective | `GraphSelectorsSpeakFromEverySelectedSpeakerAndKeepCooldown` |
| Inspector | Configuration follows node capabilities; SPECIFIC has no Room/Group filters | `InspectorCapabilitiesSummariesAndLocalWindowsStayReadable` |
| Graph readability | Configured values, bounded captions, left-aligned palette labels | Layout tests at three sizes; programmer native screenshots |
| Port help/types | Distinct localized descriptions, Импульс/Да–Нет, conversion feedback | Localization/layout tests; `KiasGraphTests` |
| Enum | Named domains and values; legacy inference; conflicting domains rejected | Typed enum tests in `KiasGraphTests` |
| Lighting | `Lighting` controls integrated fixtures; saved `LightController` controls groups | `LightingProfilesRestoreIntegratedFixtures`, existing actuator tests |
| Integrated power | Functional OFF preserves control; missing DATA/native KIAS power still blocks it | Direct Lighting regression |
| Groups | Explicit Group/current group, arbitrary names, selector invalidation and UI refresh | `ServiceGroupsUseSeparateFieldsAndRefreshRunningSelectors`, persistence tests; service native screenshots |
| AirAlarm configuration | Explicit List/Link retained for dual-capability devices | `ListModeStillCollectsAtmosDevicesWithNativeOrIntegratedPorts` (two cases) |
| Non-KIAS linking | Ordinary AirAlarm → light still works | `OrdinaryLinkModeStillLinksAnAirAlarmToALight` |
| Depressurization/recovery | Native registration/gas → AirAlarm Danger → KIAS AtmosDanger → real preset; normal gas → AtmosClear | `NativeSensorDeviceListDrivesAtmospherePresetAndRecovery` |
| Visual hierarchy | Framed sections, bounded inspector, vertical scrolling, wrapped details and tooltips | Layout tests plus five actual server BUI windows at standard/minimum sizes |
| Locale | Names/descriptions for all 91 spawnable KIAS prototypes in RU/EN; Latin KIAS | Native RU localization test plus static RU/EN key audit |

Exact commands, screenshot paths and limits of the live checks are in [VALIDATION.md](VALIDATION.md). Root causes and compatibility details are in [CORRECTION_REPORT.md](CORRECTION_REPORT.md).

No graph-runtime replacement or global power-semantics rewrite was needed. The only change outside KIAS is capability-based mode selection in `NetworkConfiguratorSystem`.
