"""Deterministic full-game workload. Missing native handlers are a hard failure."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = json.loads(Path(__file__).with_name("fullgame_contract.json").read_text(encoding="utf-8"))
DEFERRED_EVENTS = {kind for check in CONTRACT["deferredChecks"] for kind in check["events"]}
STEPS = [
    (120, "gun.fire"), (480, "gun.clear"),
    (900, "fire.ignite_suppress"), (1800, "fire.clear"),
    (2200, "atmos.depressurize_vent"), (2600, "atmos.restore"),
    (2800, "door.open"), (3000, "door.close"),
    (3300, "crew.critical"), (3420, "crew.recover"),
    (3600, "crew.death"), (3720, "crew.revive"),
    (4200, "crew.depart"), (4320, "crew.return"),
    (4500, "crew.visitor"), (4800, "crew.visitor_clear"),
    (5100, "crew.armed_threat"), (5400, "crew.threat_clear"),
    (5700, "fauna.enter"), (6000, "fauna.clear"),
    (6300, "radiation.start"), (6600, "radiation.clear"),
    (6900, "anomaly.progress"), (7500, "anomaly.clear"),
    (7800, "hull.damage"), (8100, "hull.repair"),
    (8160, "dock.dock"), (8280, "dock.undock"),
    (8400, "runtime.all_any"),
    (8490, "ammo.finite_cycle"),
    (8700, "light.power_off"), (8820, "light.power_on"),
    (8880, "light.command_off"), (8940, "light.command_on"),
    (9000, "data.cut"), (9180, "data.restore"),
    (9600, "core.power_loss"), (9780, "core.power_restore"),
    (10200, "rack.power_loss"), (10380, "rack.power_restore"),
    (10800, "geometry.breach"), (11100, "geometry.repair"),
    (11400, "ftl.visitor_in"), (14400, "ftl.visitor_out"),
    (17400, "navigation.arrive"),
    (30000, "pdc.friendly"), (30600, "pdc.hostile"),
    (31200, "pdc.blocked"), (31800, "pdc.unload"),
    (31920, "pdc.empty"), (32400, "pdc.reload"),
    (32700, "pdc.hostile"), (33300, "radio.jam"), (33600, "radio.restore"),
    (34200, "collision.damage_threshold"), (35460, "collision.clear"),
]
STEPS = [(tick, kind) for tick, kind in STEPS if kind not in DEFERRED_EVENTS]
COMPOSITE = [(0, "crew.critical"), (0, "crew.death"), (0, "fire.ignite_suppress"),
             (0, "gun.fire"),
             (0, "data.cut"), (0, "power.loss"), (2400, "geometry.breach"),
             (300, "data.restore"), (300, "power.restore"), (2700, "geometry.repair"),
             (420, "crew.recover"), (420, "crew.revive"), (480, "gun.clear"), (900, "fire.clear"),
             (1200, "dock.dock"), (1320, "dock.undock")]


def capabilities():
    declared = set()
    for name in ("NativeLab.cs", "NativeGameDrivers.cs"):
        declared.update(re.findall(r'case\s+"([^"]+)"\s*:',
                                  (ROOT / "Tools/KiasBenchmark/Server" / name).read_text(encoding="utf-8-sig")))
    required = {kind for _, kind in STEPS + COMPOSITE}
    return dict(status="PASS" if required <= declared else "INCOMPLETE",
                scope="Handler presence only; native proof and short smoke are required separately.",
                deferredChecks=CONTRACT["deferredChecks"],
                required=sorted(required), missing=sorted(required - declared))


def generate(ships, ticks, seed=20261010):
    if not 1 <= ships <= 40 or ticks < 45000:
        raise ValueError("Full-game workload requires 1..40 ships and at least 45000 measured ticks")
    check = capabilities()
    if check["missing"]:
        raise ValueError("NOT_READY: missing native handlers: " + ", ".join(check["missing"]))
    schedule = [(tick, kind, "category") for tick, kind in STEPS]
    for wave in range(36600, ticks - 3600, 6000):
        schedule.extend((wave + offset, kind, f"composite-{wave}") for offset, kind in COMPOSITE)
    events = [dict(id=f"full-{index:04d}-{ship:04d}", tick=tick,
                 ship=f"ship-{ship:04d}", type=kind, wave=wave,
                 synchronous=True,
                 **({"prototype": "AnomalyFlesh"} if kind == "anomaly.progress" else {}))
            for index, (tick, kind, wave) in enumerate(sorted(schedule))
            for ship in range(1, ships + 1)]
    state = seed & 0xffffffff
    if state == 0:
        raise ValueError("Expected nonzero 32-bit scenario seed")
    for wave in range(36600, ticks - 3600, 6000):
        for ship in range(1, ships + 1):
            state ^= (state << 13) & 0xffffffff
            state ^= state >> 17
            state ^= (state << 5) & 0xffffffff
            tick = wave + 1800 + state % 1200
            for offset, kind in ((0, "door.open"), (120, "door.close")):
                events.append(dict(id=f"background-{wave}-{ship:04d}-{offset}", tick=tick + offset,
                                   ship=f"ship-{ship:04d}", type=kind,
                                   wave=f"background-{wave}-{ship:04d}", synchronous=False))
    return sorted(events, key=lambda event: (event["tick"], event["id"]))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--ships", type=int, default=40)
    parser.add_argument("--ticks", type=int, default=108000)
    parser.add_argument("--seed", type=int, default=20261010)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.with_suffix(".capabilities.json").write_text(json.dumps(capabilities(), indent=2), encoding="utf-8")
    events = generate(args.ships, args.ticks, args.seed)
    args.output.write_text("".join(json.dumps(event) + "\n" for event in events), encoding="utf-8")
    print(json.dumps(dict(events=len(events), ships=args.ships, ticks=args.ticks)))
