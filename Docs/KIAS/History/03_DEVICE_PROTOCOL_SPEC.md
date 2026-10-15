# Device behavior contract for this patch

## Identity and discovery

Human-readable display ID: `#7A0BFC` by default (at least six hex chars), use extra characters only if ambiguous among candidates. Match full identity in search/diagnose. Example tree:

```text
Конкретные устройства
  ▾ Мостик                     ← тусклая подпись прямо после стрелки
      Динамик                   #7A0BFC
      Пожарная сигнализация     #6AD50D
  ▸ Помещение 2
  ▾ Помещение 3
      Ядро                      #A25DB9
```

IDs above illustrate format only; they must not be hardcoded. A device with changed display name retains identity. Room names are not identity and may renumber after map changes.

## Relay

- **Data:** selected `CableType` and closed/open state persist; stored relay does not affect arbitrary cable channel.
- **Manual adjust:** authorized user operates standard tool/verbs; successful change produces localized feedback naming new channel, including DATA. No popup when rejected, when no value change, or on graph automation without user actor.
- **Examine:** Shift+click detailed examine/current selection shows channel + relay state (and offline state if relevant), including when hidden subfloor. The inspect action cannot change channel.
- **Network:** opening relay splits only selected channel; changing channel while open reconnects old and splits new; closed relay connects channels normally. Do not spam whole-grid refloods if changed value equals old.

## Scanner with Connector module

- Range modification immediately affects actual eligibility and visible device membership after at most one normal cache refresh/update; no outdated auto-integrations.
- Count candidate devices on same grid, anchored, eligible, within approved range metric, scanner itself online + powered + Connector module + DATA topology. Other modules' coverage mechanics must keep working.
- `scanner A` covers target, `scanner B` also covers target => stable deterministic choice; if A loses coverage, B can take ownership; if all lose coverage, auto-connection removed. Direct kit remains direct.
- Device goes out of range due to movement, anchoring change, grid relocation, deletion or scanner role/power loss => same removal behavior.
- No duplicate subscriptions/port registrations or repeated topology invalidations when nothing changes. Do not accidentally empty normal air alarm `DeviceList` or break sensor alarm network.

## Buttons and speakers

- Explicitly identify whether faulty player scenario is `KiasEmergencyButton`, a `SignalButton`, a switch on `KiasDeviceAdapter`, or a different existing button. A displayed default recommendation must have a sink and a behavior that makes sense.
- If link is simply invalid, remove only that misleading default suggestion (prefer prototype metadata/port profile correction). If a KIAS speaker should accept it, route through real command semantics: proper message, audio channel and cooldown. Do not silently announce blank text.
- Normal button→door/sink, emergency button→emergency receiver, graph Speaker String + Announce, ALL Speaker, generic source/sink and security checks keep working.
- Graph `Speaker` currently accepts `Message:String` and `Announce/Alarm:Signal`; link-only `Signal` cannot infer a message. The UI should not imply otherwise.

## Suppression

- Activation requires online powered control endpoint, active Atmosphere role, not Testing and one authentic suppression cartridge.
- On success: consume **exactly one** cartridge, create expected foam/water effect in the proper physical coordinates/direction, emit meaningful local feedback/safety log, no duplicate activation from one impulse. Verify whether extinguishing a live fire actually occurs under default physical atmos behavior.
- On failure: no cartridge consumption, no fake positive log; optionally localized reason for authorized actor via UI/verb. Failure can be lack of role/DATA, absent cartridge, power, non-matching port, disconnected graph, ineffective reagent, and must be distinguishable in logs/tests.
- Manual alternative verb, DeviceLink sink `KiasSuppress`, controller `SPECIFIC/ALL Suppression.Trigger`, and fire alarm preset must target same implementation. Prevent ALL double consuming cartridge due to overlapping selector resolution.

## Visual prototypes

- `KiasIntegrationKit` Sprite scale ~0.6 in world (current ~0.75). Keep RSI state and sprite metadata valid; not `Item` functional size change.
- `KiasCore` 32×64, current `(0,0.5)` sprite offset. Screenshot has horizontal foreground elements crossing tall chassis; inspect actual draw order/placement before deciding. Preserve status-color overlay and wall adjacency; selection collision and click target should cover machine intelligibly.
