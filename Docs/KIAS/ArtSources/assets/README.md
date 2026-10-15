# Supplied KIAS sprites

The user supplied two 32×32 RGBA PNGs for this controller pass.

- `raw/kias_controller_rack.png` — server/controller rack.
- `raw/kias_programmable_controller.png` — removable programmable controller/card.

`rsi_ready/` contains convenience one-state RSI wrappers using the same PNG bytes:

- `KiasControllerRack.rsi`, state `rack`;
- `KiasProgrammableController.rsi`, state `controller`.

The coding agent should move/copy them to the actual Forge texture directory convention and add whatever REUSE/license metadata the repository requires. Do not invent a third-party license: these files were supplied directly by the project user for this task.
