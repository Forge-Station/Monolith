"""Plan and verify replicated native physics diagnostics independently of final ABBA."""
import argparse
import hashlib
import json
import math
import statistics
from pathlib import Path

from native_proof import lines, read, validate
from physics_report import read_run


def plan(repetitions=2, probe=False, short=False):
    if repetitions < 2:
        raise ValueError("At least two counterbalanced repetitions required")
    if probe and short: raise ValueError('Choose either functional probe or short performance study')
    start, clear, duration = (120, 780, 2400) if probe else (1800, 2460, 7200) if short else (9000, 9660, 18000)
    runs = []
    for repetition in range(1, repetitions + 1):
        order = ("A", "B", "C", "D2") if repetition % 2 else ("D2", "C", "B", "A")
        for mode in order:
            runs.append(dict(id=f"r{repetition}-{mode}", repetition=repetition, label=mode,
                             variant="A" if mode == "A" else "B",
                             physicsMode="D" if mode == "D2" else mode, queryCounters=True))
    for repetition, order in ((1, (True, False)), (2, (False, True))):
        for enabled in order:
            label = "counter-on" if enabled else "counter-off"
            runs.append(dict(id=f"r{repetition}-{label}", repetition=repetition, label=label,
                             variant="B", physicsMode="B", queryCounters=enabled))
    return dict(version=1, status="PLANNED", finalABBA=False, functionalProbe=probe,
                shortPerformanceStudy=short,repetitions=repetitions, ships=40, warmupTicks=1800, measuredTicks=duration,
                collisionTick=start, clearTick=clear, settleTicks=300, seed=20261010, runs=runs)


def write_plan(directory, repetitions, probe, short=False):
    directory.mkdir(parents=True, exist_ok=False)
    document = plan(repetitions, probe, short)
    events = [dict(id=f"physics-{kind}-{ship:04}", ship=f"ship-{ship:04}", tick=tick, type=kind)
              for tick, kind in ((document["collisionTick"], "collision.start"),
                                 (document["clearTick"], "collision.clear"))
              for ship in range(1, 41)]
    scenario = directory / "scenario.jsonl"
    scenario.write_text("".join(json.dumps(row, sort_keys=True) + "\n" for row in events), encoding="utf-8")
    document["scenarioSha256"] = hashlib.sha256(scenario.read_bytes()).hexdigest()
    (directory / "experiment-plan.json").write_text(json.dumps(document, indent=2), encoding="utf-8")
    return document


def inventory(snapshot):
    rows = snapshot["rows"]
    assert len({row["category"] for row in rows}) == len(rows), "Duplicate physics inventory category"
    return {row["category"]: tuple(row[key] for key in ("Bodies", "Fixtures", "Collidable", "Proxies"))
            for row in rows}


def spread(values):
    assert values and all(math.isfinite(value) for value in values), "Invalid variance sample"
    return dict(samples=len(values), mean=statistics.fmean(values), min=min(values), max=max(values),
                sampleStdDev=statistics.stdev(values) if len(values) > 1 else None)


def verify_run(directory, document, entry):
    path = directory / entry["id"]
    manifest = read(path / "run-manifest.json")
    started, ended = read(path / "started.json"), read(path / "completed.json")
    before, after = read(path / "physics-before.json"), read(path / "physics-final.json")
    assert manifest["physicsMode"] == entry["physicsMode"], "Physics mode mismatch"
    assert manifest["variant"] == entry["variant"], "Build variant mismatch"
    assert manifest["physicsDeviceGroup"] == "KiasPrototypes", "D2 includes native devices"
    assert manifest["physicsQueryCountersEnabled"] is entry["queryCounters"], "Counter manifest mismatch"
    assert before["queryCountersEnabled"] is entry["queryCounters"], "Engine counter toggle unavailable"
    assert after["queryCountersEnabled"] is entry["queryCounters"], "Engine counter toggle changed"
    assert after["runtimePaused"] is (entry["physicsMode"] == "C"), "Runtime diagnostic mode ineffective"
    assert manifest["diagnosticTimeScale"] == 1 and manifest["adminLogProbe"] == 0
    assert manifest["networkClients"] == 0 and manifest["nativeNpcPauseWhenNoPlayers"] is False
    assert started["scenarioSha256"] == document["scenarioSha256"], "Changed physics inputs"
    assert ended["ships"] == 40 and ended["measuredTicks"] == document["measuredTicks"]
    assert started["warmupTicks"] >= document["warmupTicks"], "Insufficient warmup"
    proof = validate(path, directory / "scenario.jsonl")
    assert proof["inputs"] == 80 and proof["nativeCompletions"] == 80, "Missing 40-ship contacts or recovery"
    if entry["queryCounters"]:
        assert after["fixtureTreeTraversals"] > before["fixtureTreeTraversals"], "Query instrumentation inactive"
    else:
        assert after["fixtureTreeTraversals"] == before["fixtureTreeTraversals"] == 0, "Disabled counters still count"
    if entry["physicsMode"] == "D":
        affected = read(path / "physics-D-affected.json")
        assert affected and any(row["previouslyCollidable"] for row in affected), "D2 changed no colliders"
        assert all(str(row["prototype"]).startswith("Kias") for row in affected), "D2 changed non-KIAS collider"
    outcomes = lines(path / "native-outcomes.jsonl")
    recovery = max(row["scheduledTick"] + row["latencyTicks"] for row in outcomes if row["type"] == "collision.clear")
    settled = recovery + document["settleTicks"]
    assert document["measuredTicks"] - settled >= 300, "Missing post-recovery idle window"
    summary = read_run(path, (document["collisionTick"], document["clearTick"], settled))
    return dict(**entry, manifest=manifest, nativeProof=proof, initialInventory=inventory(before),
                observedRecoveryTick=recovery, settledTick=settled, summary=summary)


def report(directory):
    document = read(directory / "experiment-plan.json")
    assert document["repetitions"] >= 2
    expected = plan(document["repetitions"], document["functionalProbe"],
                    document.get("shortPerformanceStudy", False))
    assert all(document[key] == value for key, value in expected.items()), "Changed experiment plan"
    rows = [verify_run(directory, document, entry) for entry in document["runs"]]
    for key in ("nativeLabSha256", "nativeGameDriversSha256"):
        assert len({row["manifest"][key] for row in rows}) == 1, f"Mismatched driver source: {key}"
    optimized = [row for row in rows if row["variant"] == "B"]
    for key in ("assemblySha256", "mapSha256", "telemetrySha256"):
        assert len({row["manifest"][key] for row in optimized}) == 1, f"Different B/C/D2 build: {key}"
    assert all(row["initialInventory"] == optimized[0]["initialInventory"] for row in optimized), "B/C/D2 starting inventory mismatch"
    variance = {}
    for label in dict.fromkeys(row["label"] for row in rows):
        same = [row for row in rows if row["label"] == label]
        variance[label] = {window: {metric: spread([row["summary"]["windows"][window][metric]["median"] for row in same])
                                   for metric in ("whole_tick_ms", "physics_ms", "observer_ms", "driver_ms")}
                           for window in ("idle_before", "collision_wave", "recovery", "idle_after")}
    calibration = {window: variance["counter-on"][window]["physics_ms"]["mean"]
                          - variance["counter-off"][window]["physics_ms"]["mean"]
                   for window in variance["counter-on"]}
    eligible = not document["functionalProbe"] and all(row["manifest"]["performanceEligible"] for row in rows)
    result = dict(status="REPLICATED_PHYSICS_DIAGNOSTIC_COMPLETE", performanceEligible=eligible,
                  fullV3Ready=False, finalABBAExecuted=False, runs=rows, varianceOfRunMedians=variance,
                  queryIncrementCalibrationDeltaMs=calibration,
                  limitations=["Run medians are observational; small repetition counts do not establish statistical significance.",
                               "A/B inventories differ; total Physics delta is not collider causality.",
                               "D2 changes physical interactions and is never a production fix.",
                               "Counter-off keeps its branch and all other observers; total harness overhead remains unresolved."],
                  productionColliderFixJustified=False)
    (directory / "replicated-physics-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    text = ["# Повторный эксперимент Physics", "", "Полный финальный ABBA не выполнялся.", "",
            "| Вариант | Повторы | Покоящиеся Physics median, среднее ± SD мс | Столкновение, мс |",
            "|---|---:|---:|---:|"]
    for label, item in variance.items():
        idle, collision = item["idle_before"]["physics_ms"], item["collision_wave"]["physics_ms"]
        text.append(f"| {label} | {idle['samples']} | {idle['mean']:.4f} ± {idle['sampleStdDev']:.4f} | {collision['mean']:.4f} |")
    text += ["", *result["limitations"]]
    (directory / "ReplicatedPhysicsReport.md").write_text("\n".join(text) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("plan", "report"))
    parser.add_argument("directory", type=Path)
    parser.add_argument("--repetitions", type=int, default=2)
    parser.add_argument("--probe", action="store_true")
    parser.add_argument("--short", action="store_true")
    args = parser.parse_args()
    result = write_plan(args.directory, args.repetitions, args.probe, args.short) if args.mode == "plan" else report(args.directory)
    print(json.dumps({key: result[key] for key in ("status", "performanceEligible") if key in result}))
