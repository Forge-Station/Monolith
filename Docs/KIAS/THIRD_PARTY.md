# KIAS references / attribution notes

This correction pass primarily uses existing Monolith/SS14 code as the implementation reference.

## UI

Shuttle console is the in-repo visual quality reference. Reuse compatible native Robust UI patterns/styles; do not copy unrelated shuttle logic.

## Device configuration

NetworkConfigurator/DeviceList is an existing native gameplay mechanism and must remain functional alongside KIAS graph DeviceLink adapters.

## Graph concepts

Wiremod/Integrated Circuits remains conceptual inspiration for typed node graphs. No external runtime/UI code should be copied blindly.

Any newly copied third-party code/assets must follow the repository's attribution/licensing rules.

## Core sprite

The new `KiasCore.rsi` sprite was generated with the built-in OpenAI imagegen tool for Forge on 2026-10-08 and exported to 32×64. Its RSI metadata records CC-BY-SA-3.0 and generated provenance. The existing controller/rack credits to Hurtsay remain separate. See [CORE_SPRITE.md](CORE_SPRITE.md) for the complete generation prompt.

## Complete KIAS sprite pack

The installed DATA cable now reuses the native MV cable geometry by MIXnikita for SS220, with CC-BY-SA-3.0 attribution preserved in `Pack/cable.rsi/meta.json`; the KIAS prototype supplies its own tint. Its sixteen connection states are copied without altering their pixel artwork. The wall preview uses `Structures/Walls/solid.rsi/full.png`, credited in that RSI to TauCetiClassic and Morb0 under CC-BY-SA-3.0.

The new `Textures/_Forge/KIAS/Pack` resources were generated with the built-in OpenAI imagegen tool and exported to native 32×32 frames, with 64×32 frames for the management console and programmer. Their metadata records CC-BY-SA-3.0 and generated provenance. Original Hurtsay controller/rack resources remain in the repository with their original credits; the new pack does not reuse that attribution. See [SPRITE_PACK.md](SPRITE_PACK.md), `SPRITE_PROMPTS.json`, and `SPRITE_ENTITY_MAP.json` for prompts and prototype coverage.
