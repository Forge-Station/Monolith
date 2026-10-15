"""Short native driver validation; does not certify full-game readiness."""
import argparse
import json
from pathlib import Path
from fullgame_scenario import COMPOSITE
from deferred_smoke_scenario import STEPS as DEFERRED_STEPS

STEPS = [
    (120, "door.open"), (240, "door.close"),
    (400, "crew.visitor"), (640, "crew.visitor_clear"),
    (800, "crew.armed_threat"), (1040, "crew.threat_clear"),
    (1200, "fauna.enter"), (1440, "fauna.clear"),
    (1600, "anomaly.progress"), (1720, "anomaly.clear"),
    (1900, "geometry.breach"), (2140, "geometry.repair"),
    (2400, "radio.jam"), (2520, "radio.restore"),
    (2800, "crew.depart"), (3040, "crew.return"),
    (3300, "data.cut"), (3480, "data.restore"),
    (3800, "pdc.friendly"), (4400, "pdc.hostile"),
    (5000, "pdc.blocked"), (5600, "pdc.unload"),
    (5720, "pdc.empty"), (6320, "pdc.reload"), (6500, "pdc.hostile"),
    (7100, "ftl.visitor_in"), (10100, "ftl.visitor_out"),
    (13200, "navigation.arrive"),
]


GROUPS = {
    "room_overlap": [(600, "geometry.breach"), (601, "door.open"), (721, "door.close"),
                     (900, "geometry.repair"), (901, "door.open"), (1021, "door.close"),
                     (1800, "geometry.breach"), (1801, "door.open"), (1921, "door.close"),
                     (2100, "geometry.repair"), (2101, "door.open"), (2221, "door.close")],
    "crew_transfer": [(120, "crew.depart"), (240, "crew.return"),
                      (600, "crew.depart"), (720, "crew.return")],
    "pdc_operator": [(120, "crew.critical"), (300, "pdc.unload"), (420, "pdc.empty"),
                     (900, "pdc.reload"), (1200, "crew.recover")],
    "magazine": [(120, "pdc.unload"), (240, "pdc.reload")],
    "pdc_controls": [(120, "pdc.friendly"), (720, "pdc.blocked"),
                     (1320, "pdc.unload"), (1440, "pdc.empty"), (2040, "pdc.reload")],
    "world": STEPS,
    "vent": [(120, "atmos.depressurize_vent"), (2040, "atmos.restore")],
    "fire": [(120, "fire.ignite_suppress"), (1380, "fire.clear")],
    "combat": [(120, "pdc.friendly"), (720, "pdc.hostile"),
               (1320, "pdc.blocked"), (1920, "pdc.unload"),
               (2040, "pdc.empty"), (2640, "pdc.reload"), (2820, "pdc.hostile")],
    "collision": [(120, "collision.damage_threshold"), (1380, "collision.clear")],
    "fanout": [(120, "runtime.all_any")],
    "crew_power": [(120, "crew.critical"), (300, "crew.recover"),
                   (480, "crew.death"), (720, "crew.revive"),
                   (1500, "data.cut"), (1680, "data.restore"),
                   (2100, "core.power_loss"), (2280, "core.power_restore"),
                   (2700, "rack.power_loss"), (2880, "rack.power_restore")],
    "gun": [(120, "gun.fire"), (480, "gun.clear")],
}
DURATIONS = {"pdc_controls": 3000, "magazine": 600, "world": 26000, "vent": 4200, "fire": 2760,
             "combat": 4200, "collision": 3960, "fanout": 2100,
             "crew_power": 3600, "gun": 1500}
DURATIONS["crew_transfer"] = 1200
DURATIONS["room_overlap"] = 3000
GROUPS["systems"] = [(tick + offset, kind)
                     for group, offset in (("fanout", 0), ("crew_power", 2100), ("gun", 5700))
                     for tick, kind in GROUPS[group]]
DURATIONS["systems"] = 7200
GROUPS["composite"] = [(tick + 120, kind) for tick, kind in COMPOSITE]
DURATIONS["composite"] = 3900
DURATIONS["pdc_operator"] = 1800
GROUPS["navigation_collision"] = [(120, "navigation.arrive"), (12360, "collision.damage_threshold"), (13620, "collision.clear")]
DURATIONS["navigation_collision"] = 15000
GROUPS["collision_rotated"] = GROUPS["collision"]
DURATIONS["collision_rotated"] = DURATIONS["collision"]
GROUPS["deferred"] = DEFERRED_STEPS
DURATIONS["deferred"] = 11500


def generate(ships, group="world"):
    if not 1 <= ships <= 40:
        raise ValueError("Expected 1..40 native ships")
    return [dict(id=f"world-smoke-{index:03d}-{ship:04d}", tick=tick,
                 ship=f"ship-{ship:04d}", type=kind,
                 **({"fixtureRotationDegrees": (ship * 37) % 360 if ship % 2 else (ship - 20) * .003} if group == "collision_rotated" and kind == "collision.damage_threshold" else {}),
                 **({"prototype": "AnomalyFlesh"} if kind == "anomaly.progress" else {}))
            for index, (tick, kind) in enumerate(GROUPS[group])
            for ship in range(1, ships + 1)]


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--ships", type=int, default=1)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--group", choices=GROUPS, default="world")
    args = parser.parse_args()
    events = generate(args.ships, args.group)
    args.output.write_text("".join(json.dumps(event) + "\n" for event in events), encoding="utf-8")
    print(json.dumps(dict(events=len(events), group=args.group, measuredTicks=DURATIONS[args.group],
                         scope="PARTIAL_NATIVE_WORLD_SMOKE; full-game readiness requires all contract scenarios")))
