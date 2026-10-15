# Delivery report template — fill with actual evidence only

## Environment

- Branch / HEAD at start and end:
- Dirty files initially / user changes preserved:
- Runtime platform, .NET SDK version, build mode:
- Actual wall-clock execution start/end and model usage if measurable (**do not invent tokens**):

## Result table

| TODO | Changed files | Root cause (actual test/log) | Proof (named test/game screenshot) | Status |
| --- | --- | --- | --- | --- |
| T01 Compact UI IDs | | | | PASS/FAIL/UNVERIFIED |
| T02 Room hierarchy | | | | |
| T03 Palette width/readability | | | | |
| T04 `Шаблон:` | | | | |
| T05 Integration kit -20% | | | | |
| T06 Core 32×64 world layers | | | | |
| T07 Relay popup/examine | | | | |
| T08 Scanner zone detach | | | | |
| T09 Switch→speaker recommendation | | | | |
| T10 Physical suppression | | | | |

## Commands + proof

```text
Baseline test command / output / counts:
Red tests (captured before fix):
Focused green tests:
Entire KIAS suite command / counts:
git diff --check:
Actual Scripts/bat/buildAllRelease.bat command, working dir, exit code, log path:
Actual Scripts/bat/runQuickAll.bat command, working dir, process evidence, log path:
Game connection/UI smoke screenshot paths:
```

Never report `runQuickAll.bat PASS` just because the launcher started; verify actual client and server. Never substitute `dotnet build` for requested real `.bat` proof. Release build script may block at `pause`; capture true completion.

## Risk register evidence

- Scanning topology revision after 30 ticks, any loops/idle churn:
- Two scanners and direct kit priority:
- Sprite before/after layer/placement and selection:
- Real fire produced and suppressed, cartridge before/after:
- Button→speaker recommendation root cause (exact source/sink/port):
- Other gameplay (non-KIAS DeviceLink/DeviceList) protected:
- Performance difference (numbers if measured, otherwise NOT MEASURED):

## Residual problems / stop blockers

Keep short, concrete: repro steps, observed behavior, failing logs, likely impacted files. Never quietly mark untested issues fixed.
