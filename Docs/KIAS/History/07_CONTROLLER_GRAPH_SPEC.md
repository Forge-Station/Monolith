# Programmer graph/editor UX — acceptance specification

## Hierarchy must read without hover

Use clear visual emphasis at least on standard 1200×720:

```text
КОНКРЕТНЫЕ УСТРОЙСТВА                      [поиск]

  ▾ Мостик                                  ← серая, readable label beside arrow
      Динамик             #7A0BFC           ← primary readable name + subdued alias
      Сканер              #6AD50D
  ▸ Медицинский отсек
  ▾ Помещение 3
      Реле                #C7A001
```

Room rows are nested under section, devices visibly nested under room. Enforce real indentation with margin/padding/containers rather than spaces in localized text. Heading name right next to disclosure arrow, not in distant/misaligned column. Long names: wrap naturally or reserve a second line for profile/alias; ellipsis+tooltip only secondary fallback. Keep full ID accessible. Named rooms first; unnamed sorted room number; preserve search filtering. Collapsed group state remains across repeated update.

## Width and responsive behaviour

Current main FancyWindow 1200×720 (min 850×500), palette 215–250, inspector 300–340. A modest palette 280–320px can help **at standard width**, but at 850 width inspector and canvas must not be squeezed off-screen. Prefer native responsive layout or a proven native splitter; inspect how Robust handles MinSize, HorizontalExpand and scroll container. If implementing splitter: store user preference locally, clamp to window/canvas min, no cursor event leak, no state corruption on refresh. Otherwise choose clear safe responsive widths; left area wider when available, smaller when narrow, multi-line list rows in narrow mode. Validate actual screenshot, including RU fonts and 125% scaling.

## Header controls

- `_presets` dropdown gets nearby visible `Шаблон:` / `Template:` label, same height and group/panel.
- Native per-state `KiasUi.Panel`/`KiasUi.Field` reusable styling preferred; do not create parallel fake controls.
- `WRITE`, `Discard`, `Eject` semantics unchanged; see previous regression after card name sync.

## Graph node names

- SPECIFIC node: human device name primary, concise ID and status secondary; do not hardcode stale Name; update on valid state and keep persisted fallback when original device disappears.
- ANY/ALL selector semantics must remain as implemented, including separate `Lighting` and `LightController` physical group controllers.
- Do not change native port IDs/schema just to rename visible labels. Localize user-facing names/port descriptions. Do not reintroduce `Label × #12hex ...` walls of text.

## Interaction quality

- Selecting device row adds correct SPECIFIC device (by bound entity, NOT short label).
- Search full ID and prefix works, including Cyrillic case-insensitive match. Search should not rebuild the node canvas or reset scroll unnecessarily.
- Keyboard focus/search caret stable on state refresh; palette item hover tooltip does not remain stuck after moving cursor off it.
- Port connector drag, pan/zoom, node selection and wire error feedback remain usable even with adjusted widths.
- Use reproducible UI screenshot checks for 850×500, 1024×600, 1200×720, 1600×900; justify any case that remains intentionally clipped.
