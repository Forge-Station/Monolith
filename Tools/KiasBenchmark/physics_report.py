"""Report controlled diagnostic rows with explicit coverage and confidence limits."""
import argparse
import csv
import json
import math
import statistics
from pathlib import Path

from fullgame_report import tick_statistics
from native_proof import read, validate


def quantile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def distribution(values):
    return {"mean": statistics.fmean(values), "median": statistics.median(values),
            "p95": quantile(values, .95), "p99": quantile(values, .99),
            "p999": quantile(values, .999), "max": max(values)}


def read_run(path, boundaries=(9000, 9660, 12000)):
    manifest = json.loads((path / "run-manifest.json").read_text(encoding="utf-8-sig"))
    completed = json.loads((path / "completed.json").read_text(encoding="utf-8-sig"))
    before = json.loads((path / "physics-before.json").read_text(encoding="utf-8-sig"))
    after = json.loads((path / "physics-final.json").read_text(encoding="utf-8-sig"))
    with (path / "ticks.csv").open(encoding="utf-8-sig", newline="") as stream:
        ticks = [{key: float(value) for key, value in row.items()} for row in csv.DictReader(stream)]
    assert len(ticks) == completed["measuredTicks"]
    assert [row["relative_tick"] for row in ticks] == list(range(len(ticks))), "Tick sequence mismatch"
    assert all(math.isfinite(row[key]) and row[key] >= 0 for row in ticks
               for key in ("whole_tick_ms", "physics_ms", "content_engine_ms", "driver_ms", "observer_ms",
                           "physics_allocated_bytes", "whole_allocated_bytes", "gc_pause_ms")), "Invalid physics metric"
    assert before["fixtureTreeTraversals"] is not None and after["fixtureTreeTraversals"] is not None
    totals = lambda snapshot: {key: sum(row[key] for row in snapshot["rows"])
                               for key in ("Bodies", "Awake", "Fixtures", "Collidable", "Proxies", "Contacts")}
    collision, clear, settled = boundaries
    assert 0 < collision < clear < settled < len(ticks), "Invalid physics phase boundaries"
    windows = {"idle_before": [row for row in ticks if row["relative_tick"] < collision],
               "collision_wave": [row for row in ticks if collision <= row["relative_tick"] < clear],
               "recovery": [row for row in ticks if clear <= row["relative_tick"] < settled],
               "idle_after": [row for row in ticks if row["relative_tick"] >= settled]}
    report = dict(directory=str(path), mode=manifest["physicsMode"], ships=completed["ships"],
                  measuredTicks=len(ticks), performanceEligible=manifest["performanceEligible"],
                  before=totals(before), after=totals(after),
                  inventoryScopes={scope: {key: sum(row[key] for row in before["rows"]
                                                       if row["category"].startswith(scope))
                                            for key in ("Bodies", "Awake", "Fixtures", "Collidable", "Proxies", "Contacts")}
                                   for scope in ("lab_fleet/active/", "lab_fleet/paused/", "other/active/", "other/paused/")},
                  fixtureTreeTraversals=after["fixtureTreeTraversals"] - before["fixtureTreeTraversals"],
                  windows={})
    for name, rows in windows.items():
        assert rows, f"Missing {name} window"
        report["windows"][name] = {key: distribution([row[key] for row in rows])
                                  for key in ("whole_tick_ms", "physics_ms", "content_engine_ms",
                                              "driver_ms", "observer_ms", "whole_allocated_bytes", "gc_pause_ms")}
        report["windows"][name]["ticksOverBudget"] = sum(row["whole_tick_ms"] > 1000 / 60 for row in rows)
    top = sorted(ticks, key=lambda row: row["whole_tick_ms"], reverse=True)[:100]
    with (path / "top100-ticks.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(top[0]))
        writer.writeheader()
        writer.writerows(top)
    (path / "physics-summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def compare(root):
    rows = [read_run(root / mode) for mode in "ABCD"]
    assert all(row["ships"] == 40 and row["measuredTicks"] >= 18000 for row in rows)
    manifests = [json.loads((root / mode / "run-manifest.json").read_text(encoding="utf-8-sig")) for mode in "ABCD"]
    assert len({manifest["nativeLabSha256"] for manifest in manifests}) == 1
    assert len({manifest["nativeGameDriversSha256"] for manifest in manifests}) == 1
    assert len({manifest["mapSha256"] for manifest in manifests[1:]}) == 1
    snapshots = [json.loads((root / mode / "physics-before.json").read_text(encoding="utf-8-sig")) for mode in "BCD"]
    inventory = [{row["category"]: row for row in snapshot["rows"]} for snapshot in snapshots]
    structural_keys = ("Bodies", "Fixtures", "Collidable", "Proxies")
    differences = []
    for category in sorted(set().union(*(set(items) for items in inventory))):
        for key in (*structural_keys, "Awake", "Contacts"):
            values = {mode: items.get(category, {}).get(key, 0) for mode, items in zip("BCD", inventory)}
            if len(set(values.values())) > 1:
                differences.append(dict(category=category, field=key, values=values,
                                        structural=key in structural_keys))
    comparable = not any(item["structural"] for item in differences)
    affected = json.loads((root / "D" / "physics-D-affected.json").read_text(encoding="utf-8-sig"))
    changed = [item for item in affected if item["previouslyCollidable"]]
    integrated_native = [item for item in changed if not str(item["prototype"]).startswith("Kias")]
    diagnostic_scope = dict(changedCollidableEntities=len(changed),
                            kiasPrototypeEntities=len(changed) - len(integrated_native),
                            integratedNativePrototypeEntities=len(integrated_native),
                            kiasOnlyScope=not integrated_native)
    (root / "starting-inventory-differences.json").write_text(json.dumps(differences, indent=2), encoding="utf-8")
    lines = ["# Physics A/B/C/D — diagnostic evidence", "",
             "FULL GAME FINAL RUN NOT YET EXECUTED", "",
             "| Mode | Bodies | Fixtures | Proxies | Idle physics mean ms | Collision physics mean ms | Whole p99 ms |",
             "|---|---:|---:|---:|---:|---:|---:|"]
    for row in rows:
        windows = row["windows"]
        lines.append(f"| {row['mode']} | {row['before']['Bodies']} | {row['before']['Fixtures']} | {row['before']['Proxies']} | "
                     f"{windows['idle_before']['physics_ms']['mean']:.4f} | {windows['collision_wave']['physics_ms']['mean']:.4f} | "
                     f"{windows['idle_before']['whole_tick_ms']['p99']:.4f} |")
    lines += ["", "One run per mode: descriptive results only; run-to-run variance and confidence are unresolved.",
              f"B/C/D structural starting inventory parity: {'PASS' if comparable else 'FAIL'}. "
              f"All {len(differences)} category differences, including awake bodies and contacts, are retained in starting-inventory-differences.json.",
              f"D changed {len(changed)} collidable entities, including {len(integrated_native)} integrated native devices "
              "(doors, engines, lights, etc.). This broad diagnostic does NOT isolate KIAS-only prototype colliders. A restricted D2 is still required.",
              "A/B inventory differences do not establish collider causality. D is diagnostic only and changes physical interactions.",
              "Fixture tree traversals include broadphase collision queries. Counter overhead is symmetric but requires separate calibration.",
              "Physics time is nested inside content time. Driver/observer costs are separate; phase totals must not be added blindly.",
              "No production collider change is justified by this report alone."]
    (root / "PhysicsReport.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (root / "physics-comparison.json").write_text(json.dumps(dict(
        status="DIAGNOSTIC_ONLY_CONFIDENCE_INCOMPLETE" if comparable else "INVALID_STARTING_INVENTORY_MISMATCH",
        structuralStartingInventoryParity=comparable, startingInventoryDifferences=differences,
        diagnosticDScope=diagnostic_scope, runs=rows), indent=2), encoding="utf-8")
    if not comparable:
        raise ValueError("B/C/D structural inventories differ; diagnostic evidence saved, causal comparison invalid")


def narrow_smoke(root):
    rows = []
    manifests = []
    inventories = []
    for label in ("B", "D2"):
        path = root / label
        manifest = read(path / "run-manifest.json")
        completed = read(path / "completed.json")
        assert completed["ships"] == 40, "Restricted diagnostic requires 40 Briar"
        assert manifest["physicsMode"] == ("B" if label == "B" else "D")
        if label == "D2":
            assert manifest["physicsDeviceGroup"] == "KiasPrototypes", "D2 scope includes native devices"
        snapshot = read(path / "physics-before.json")
        inventories.append({item["category"]: tuple(item[key] for key in ("Bodies", "Fixtures", "Collidable", "Proxies"))
                            for item in snapshot["rows"]})
        rows.append(dict(label=label, nativeProof=validate(path, root / "scenario.jsonl"),
                         statistics=tick_statistics(path / "ticks.csv", completed["measuredTicks"]),
                         instrumentationSnapshotMs=snapshot["instrumentationMs"],
                         instrumentationSnapshotBytes=snapshot["instrumentationBytes"]))
        manifests.append(manifest)
    for key in ("assemblySha256", "mapSha256", "nativeLabSha256", "nativeGameDriversSha256", "telemetrySha256"):
        assert manifests[0][key] == manifests[1][key], f"D2 uses a different build or fixture: {key}"
    assert inventories[0] == inventories[1], "B/D2 structural starting inventories differ"
    affected = read(root / "D2" / "physics-D-affected.json")
    changed = [item for item in affected if item["previouslyCollidable"]]
    assert changed and all(item["prototype"].startswith("Kias") for item in affected), "D2 changed non-KIAS prototype colliders"
    prototypes = {}
    for item in changed:
        prototypes[item["prototype"]] = prototypes.get(item["prototype"], 0) + 1
    result = dict(status="RESTRICTED_D2_NATIVE_SMOKE_COMPLETE", performanceEligible=False,
                  scope="Actual collision and KIAS-only scope verification; repeated timing variance and overhead calibration remain required",
                  structuralStartingInventoryParity=True, changedCollidableEntities=len(changed),
                  changedPrototypes=prototypes, productionColliderFixJustified=False, runs=rows)
    (root / "narrow-smoke-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    text = ["# Узкий Physics D2: короткая физическая проверка", "", "FULL GAME FINAL RUN NOT YET EXECUTED.", "",
            "Одна и та же сборка B, карта и игровые драйверы; D2 отключает только коллайдеры прототипов Kias*.",
            f"Исходный структурный инвентарь совпадает. Изменено коллайдеров: {len(changed)}.",
            "Настоящие столкновения проверены в обоих запусках. Это не приёмка производительности.",
            "Повторные измерения, разброс и отдельная калибровка накладных расходов ещё обязательны.",
            "Изменение производственных коллайдеров этими данными не обосновано.", "", "Изменённые прототипы:"]
    text.extend(f"- {prototype}: {count}" for prototype, count in sorted(prototypes.items()))
    (root / "NarrowPhysicsSmoke.md").write_text("\n".join(text) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--narrow-smoke", action="store_true")
    args = parser.parse_args()
    if args.narrow_smoke:
        result = narrow_smoke(args.directory)
        print(json.dumps({key: result[key] for key in ("status", "performanceEligible", "changedCollidableEntities")}))
    else:
        compare(args.directory)
