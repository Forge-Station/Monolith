"""One short, synchronous native driver probe. This is not full-game acceptance."""
import argparse
import json
from pathlib import Path

STEPS = [
    (120, "gun.fire"), (480, "gun.clear"),
    (600, "crew.critical"), (720, "crew.recover"),
    (900, "crew.death"), (1020, "crew.revive"),
    (1800, "data.cut"), (1980, "data.restore"),
    (2400, "core.power_loss"), (2580, "core.power_restore"),
    (3000, "rack.power_loss"), (3180, "rack.power_restore"),
    (3600, "ftl.in"), (4800, "ftl.out"), (5700, "ftl.clear"),
    (6000, "collision.start"), (6660, "collision.clear"),
]


def generate(ships):
    if not 1 <= ships <= 40:
        raise ValueError("Expected 1..40 real ships")
    return [dict(id=f"smoke-{index:03d}-{ship:04d}", tick=tick,
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
    print(json.dumps(dict(events=len(events), measuredTicks=7200,
                         scope="PARTIAL_NATIVE_SMOKE; low-level FTL only; no full-game claim")))
