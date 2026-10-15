# Clean code, contributor guidelines, assets and source references

## Monolith contribution conventions

In actual local branch, check `.github/PULL_REQUEST_TEMPLATE.md`, README, `.editorconfig`, `global.json`, tests, existing C# style, localization and prototype workflows. A repository root `CONTRIBUTING.md` and `.github/CONTRIBUTING.md` were NOT found via GitHub file lookups on snapshot; do not invent their requirements. Upstream Monolith README publicly refers to Space Wizards general dev environment, while Monolith's own scripts and .NET version are authoritative for this checkout. Current repo source always beats generalized docs.

Useful repo references:

- Fork: https://github.com/theg33ka/Monolith/tree/KIAS
- Monolith upstream: https://github.com/Forge-Station/Monolith
- Existing repo UI reference: `Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml`.
- Existing gameplay wiring: `Content.Server/DeviceLinking/Systems/DeviceLinkSystem.cs`, `Content.Server/DeviceNetwork/Systems/NetworkConfiguratorSystem.cs`.

## No AI code-smell shortcuts

- No new blanket `FixedVoice`-style flags on shared components for one KIAS specific interaction.
- No O(n²) nearest-scanner/world scans every tick, string GUID regeneration, broad ECS queries for each graph event.
- No duplicate hardcoded protocol layer in addition to graph presets; no duplicate device components when existing one suffices.
- Prefer small local systems, immutable/explicit schema where needed, idempotent mutations, server authority, graceful fail states and localized user feedback.
- No hiding failures by logging everything as success; no test-specific production hacks/debug commands left in repo.

## Sprite / licensing

- Existing `Docs/KIAS/CORE_SPRITE.md` documents 32×64 RSI with authored pixel art; maintain existing attribution. Do not reassign authors of original RSI/sprites.
- This patch modifies `KiasIntegrationKit` prototype scale, likely requires no art generation, and adjusts core placement/layer only after verifying screenshot in game.
- If touching RSI metadata, preserve state names and pixel sizes, licensed artwork and credit information; avoid unlicensed external sprite/texture imports.
- Our three `references/*.png` are **user screenshots supplied for bug reproduction**, not new game-ready assets. Do not ship these into the game resource tree.

## Diagnostic honesty

These docs cite actual paths read from branch snapshot; they do not claim live test reproduction. End-user screenshots show reported symptoms, not internal root causes. Tests and application are the sole source for completion status.
