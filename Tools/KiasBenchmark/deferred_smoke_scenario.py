"""Short native acceptance for previously deferred requirements; no ABBA claim."""
import argparse
import json
from pathlib import Path

STEPS = [
    (120, "radiation.start"), (840, "radiation.clear"),
    (1560, "radiation.start"), (2280, "radiation.clear"),
    (3000, "light.power_off"), (3720, "light.power_on"),
    (4440, "light.command_off"), (5160, "light.command_on"),
    (5880, "hull.damage"), (6600, "hull.repair"),
    (7320, "dock.dock"), (8040, "dock.undock"),
    (8760, "ammo.finite_cycle"),
]


def generate(ships=1):
    if not 1 <= ships <= 40:
        raise ValueError("Expected 1..40 native ships")
    return [dict(id=f"deferred-{index:02d}-{ship:04d}", tick=tick,
                 ship=f"ship-{ship:04d}", type=kind)
            for index, (tick, kind) in enumerate(STEPS)
            for ship in range(1, ships + 1)]


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--ships", type=int, default=1)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    events = generate(args.ships)
    args.output.write_text("".join(json.dumps(event) + "\n" for event in events), encoding="utf-8")
    print(json.dumps(dict(events=len(events), measuredTicks=11500, scope="SHORT_DEFERRED_ACCEPTANCE")))
