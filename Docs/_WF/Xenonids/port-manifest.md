# Xenonids port manifest

Where the Xenonids module's assets, locale and staged prototypes come from and under which licence. Content files carry
no licence headers; this manifest is the record. The C# ports are recorded when they land.

## Sources

- RMC-14, commit `7fca80389aa705890666b9476051fcca2970e6fc` of 2026-10-15, engine v264. MIT. Textures, audio, locale
  and prototypes of the xenonids (every caste, the hive, weeds, resin structures, eggs, facehuggers, evolution, plasma,
  abilities), plus the shields, stun, slow and pulling content the xeno code leans on.
- A few staged dependency blocks are RMC-14's copies of upstream Space Station 14 prototypes (alert categories, for
  example); upstream is MIT, Copyright (c) 2017-2025 Space Wizards Federation (`LICENSE-UPSTREAM.TXT` of RMC-14).

Wolfgate as a whole is AGPL-3.0, which admits MIT. Sprites keep the licence and copyright in their RSI `meta.json`
(copied byte for byte; nothing inside an image or `meta.json` is rewritten). Audio attributions are in
`Resources/Audio/_WF/Xenonids/attributions.yml`, built from RMC-14's `attributions.yml` entries for the copied files.
Six xeno sounds have no entry in RMC-14 itself (`Xeno/burrowing_b.ogg`, `Xeno/burrowoff.ogg`, `Xeno/pry1.ogg` to
`Xeno/pry4.ogg`) and so none here; their origin still needs finding.

## RMC-14 licence notice

```
MIT License

Copyright (c) 2023-2026 RMC-14

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## What was copied

The folders are the record; there is no per-file table. Each script is rerunnable and prints what it did.

| Area | Destination | Count | Script |
|---|---|---|---|
| Textures | `Resources/Textures/_WF/Xenonids/` (RMC's `_RMC14/` sub path kept) | 135 RSI folders, 2496 files | `Tools/_WF/Xenonids/copy_assets.py` |
| Audio | `Resources/Audio/_WF/Xenonids/` (`Xeno`, `Voice/Xeno` and the other sounds the xeno sources name) | 137 ogg files, 131 attribution entries | `Tools/_WF/Xenonids/copy_assets.py` |
| Locale | `Resources/Locale/en-US/_WF/Xenonids/` (RMC's file names and sub-folders; `rmc-shared.ftl` holds keys RMC defines outside its xeno files) | 29 ftl files, 915 keys | `Tools/_WF/Xenonids/copy_locale.py` |
| Prototypes | `Resources/Prototypes/_WF/Xenonids/` (staged outside `Resources` during the port, then moved lane by lane) | 95 files, 62 dependency blocks staged | `Tools/_WF/Xenonids/stage_prototypes.py` (used once) |
| Component audit | `Docs/_WF/Xenonids/component-audit.md` | 497 components | `Tools/_WF/Xenonids/component_audit.py` |

Textures not copied: `Actions/xeno_emotes.rsi`, which Wolfgate already has byte for byte under `_RMC14`; prototypes
keep pointing at that path (`Tools/_WF/Xenonids/asset_reuse.txt`, read by `import_rmc.py`). `xeno_figurines.rsi` is
skipped with the figurine prototypes.

No Wolfgate locale key collides with the copied ones, so nothing was dropped.

`import_rmc.py` was extended for this: in `.yml` it now also rewrites the relative `sprite: _RMC14/...` form and
`!type:Content.*._RMC14...` type names, and honours `asset_reuse.txt`.

## Staging

RMC's xeno prototypes were first staged in `Tools/_WF/Xenonids/staging/` (outside `Resources`, so the linter and
server did not load them) and moved into `Resources/Prototypes/_WF/Xenonids` with the lane that brought their C#. The
integration lane emptied it and deleted the folder; its last stale copies (crusher, defender, praetorian) had already
landed, and the remaining action blocks were converted into `Actions/Xeno/xeno_p4_actions.yml`. RMC xeno prototypes
deliberately not ported: guidebook entries (`RMCGuidebookAction*`, `GuideHelp`), xeno figurines, `fun.yml`, survivor
jobs and `Roles/Jobs/Survivor`, the night-vision alerts (Wolfgate's night vision is used), job groups (no `JobGroup`
here), tactical map actions and alerts (`RMCActionOpenTacticalMap*`, `AreaInfo`), dropship hijack, the parasite HTN
compounds, `CMDepartmentBase`/`CMJobBase`, the `RMCXeno` NPC faction and the `Hivemind` radio channel.
