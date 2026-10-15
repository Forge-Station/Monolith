"""Collect native FullGame ABBA evidence; preflight certification remains separate."""
import argparse
import csv
import json
import math
import html
from collections import defaultdict
from pathlib import Path

from fullgame_scenario import capabilities
from load_report import summarize
from native_proof import lines, read, validate
from network_proof import validate as validate_network
from world_smoke_scenario import generate as smoke_scenario


def tick_statistics(path, expected):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        rows = list(csv.DictReader(stream))
    assert len(rows) == expected, "Missing tick samples"
    assert [int(row["relative_tick"]) for row in rows] == list(range(expected)), "Tick sequence mismatch"
    columns = ("whole_tick_ms", "physics_ms", "driver_ms", "observer_ms", "whole_allocated_bytes",
               "thread_allocated_bytes", "gc_pause_ms")
    for row in rows:
        for name in columns:
            number = float(row[name])
            assert math.isfinite(number) and number >= 0, f"Invalid tick metric {name}"
    longest = current = 0
    for row in rows:
        current = current + 1 if float(row["whole_tick_ms"]) > 1000 / 60 else 0
        longest = max(longest, current)
    return dict(wholeTick=summarize([float(row["whole_tick_ms"]) for row in rows]),
                physics=summarize([float(row["physics_ms"]) for row in rows]),
                longestConsecutiveOverBudgetTicks=longest,
                allocationsBytes=sum(int(row["whole_allocated_bytes"]) for row in rows),
                threadAllocationsBytes=sum(int(row["thread_allocated_bytes"]) for row in rows),
                gcPauseMs=sum(float(row["gc_pause_ms"]) for row in rows),
                top100WorstTicks=sorted(rows, key=lambda row: float(row["whole_tick_ms"]), reverse=True)[:100])


def wave_statistics(inputs, events, outcomes, ships):
    delivered = {event["id"]: event for event in events}
    completed = {outcome["id"]: outcome for outcome in outcomes}
    groups = defaultdict(list)
    for item in inputs:
        groups[(item["wave"], item["tick"], item["type"])].append(item)
    result = []
    for (wave, tick, kind), items in groups.items():
        synchronous = all(item.get("synchronous", True) for item in items)
        expected = ships if synchronous else 1
        assert len(items) == expected and len({item["ship"] for item in items}) == expected, "Incomplete fleet wave"
        starts = [delivered[item["id"]] for item in items]
        assert {event["actualTick"] for event in starts} == {tick}, "Wave dispatch is not synchronous"
        assert len({event["engineTick"] for event in starts}) == 1, "Wave starts span engine ticks"
        native = [completed[item["id"]] for item in items if item["id"] in completed]
        result.append(dict(wave=wave, tick=tick, type=kind, synchronous=synchronous, planned=len(items), delivered=len(starts),
                           asynchronousCompleted=len(native),
                           latencyTicks=[outcome["latencyTicks"] for outcome in native],
                           immediateEffects=[event["nativeEffect"] for event in starts
                                             if not event["nativeEffect"].get("asynchronous")]))
    return result


def smoke_report(directory):
    state = read(directory / "suite-status.json")
    assert state["results"] and all(run["status"] == "PASS" for run in state["results"]), "Incomplete native smoke suite"
    pair = read(directory / "baseline-manifest.json")
    runs = []
    for run in state["results"]:
        path = Path(run["path"])
        scenario = directory / (run["group"] + ".jsonl")
        proof = validate(path, scenario)
        start = read(path / "started.json")
        fingerprints = read(path / "run-manifest.json")
        build = next(item for item in pair["builds"] if item["label"] == run["variant"])
        for name in ("assemblySha256", "nativeLabSha256", "nativeGameDriversSha256"):
            assert fingerprints[name] == build[name], f"Changed smoke build or harness: {name}"
        inputs = [dict(item, wave=run["group"], synchronous=True) for item in lines(scenario)]
        final = read(path / "final-telemetry.json") if run["variant"] == "B" else None
        if run["variant"] == "B":
            verify_scaling_recovery(final, start["ships"])
        runs.append(dict(variant=run["variant"], group=run["group"], ships=start["ships"],
                         wallSeconds=run["wallSeconds"], nativeProof=proof, finalTelemetry=final,
                         fingerprints=fingerprints, statistics=tick_statistics(path / "ticks.csv", start["measuredTicks"]),
                         waves=wave_statistics(inputs, lines(path / "events.jsonl"),
                                               lines(path / "native-outcomes.jsonl"), start["ships"])))
    result = dict(status="NATIVE_SMOKE_REPORT_COMPLETE", fullGameplayCoverage=False,
                  performanceEligible=False, scope="Short native suite only; not final ABBA or performance acceptance",
                  deferredChecks=capabilities()["deferredChecks"], runs=runs)
    (directory / "smoke-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    rows = []
    for run in runs:
        ticks = run["statistics"]["wholeTick"]
        rows.append(f'<tr><td>{html.escape(run["group"])} {run["variant"]}</td><td>{run["ships"]}</td>'
                    f'<td>{run["nativeProof"]["inputs"]}</td><td>{ticks["medianMs"]:.3f}</td>'
                    f'<td>{ticks["p99Ms"]:.3f}</td><td>{ticks["maxMs"]:.3f}</td></tr>')
    page = ('<!doctype html><meta charset="utf-8"><title>KIAS native smoke</title>'
            '<h1>Короткие игровые проверки</h1><p>FULL GAME FINAL RUN NOT YET EXECUTED.</p>'
            '<p>Эти данные не являются приёмкой производительности или полного покрытия.</p>'
            '<p><a href="smoke-results.json">Доказательства, волны, очереди, метрики и 100 худших тиков</a></p>'
            '<table border="1"><tr><th>Проверка</th><th>Корабли</th><th>Воздействия</th><th>p50, мс</th><th>p99</th><th>max</th></tr>'
            + ''.join(rows) + '</table>')
    (directory / "smoke-report.html").write_text(page, encoding="utf-8")
    return result


def scaling_report(directory):
    pair = read(directory / "baseline-manifest.json")
    build = next(item for item in pair["builds"] if item["label"] == "B")
    runs = []
    for ships in (1, 10, 20, 40):
        path = directory / f"B{ships}"
        scenario = directory / f"scenario-{ships}.jsonl"
        assert lines(scenario) == smoke_scenario(ships, "crew_power"), "Scaling inputs changed"
        manifest, started = read(path / "run-manifest.json"), read(path / "started.json")
        assert manifest["variant"] == manifest["physicsMode"] == "B", "Scaling diagnostic mode changed"
        assert manifest["performanceEligible"] is False, "Functional scaling must not certify performance"
        assert manifest["diagnosticTimeScale"] == 1 and manifest["nativeNpcPauseWhenNoPlayers"] is False
        assert started["ships"] == ships and started["warmupTicks"] >= 1800
        assert started["measuredTicks"] == 3600, "Scaling duration changed"
        for name in ("assemblySha256", "nativeLabSha256", "nativeGameDriversSha256", "telemetrySha256"):
            assert manifest[name] == build[name], f"Scaling build mismatch: {name}"
        assert manifest["mapSha256"] == pair["staticParity"]["bMapSha256"], "Scaling map changed"
        proof = validate(path, scenario)
        assert proof["inputs"] == proof["nativeCompletions"] == ships * 10, "Scaling native recovery incomplete"
        final = read(path / "final-telemetry.json")
        verify_scaling_recovery(final, ships)
        outcomes = lines(path / "native-outcomes.jsonl")
        runs.append(dict(ships=ships, nativeProof=proof, runningCards=final["runningCards"],
                         faultedCards=final["faultedCards"], queues=final["queues"],
                         maxNativeLatencyTicks=max(item["latencyTicks"] for item in outcomes),
                         statistics=tick_statistics(path / "ticks.csv", started["measuredTicks"])))
    result = dict(status="BRIAR_FUNCTIONAL_SCALING_COMPLETE_EIGHT_HULLS_OPEN", fullV3Ready=False,
                  performanceEligible=False, fullFinalExecuted=False, hullClasses=["Briar"],
                  buildSourceFingerprint=pair["sourceFingerprint"], runs=runs,
                  scope="Functional crew/DATA/power scaling only; eight hull classes and clean-host performance remain required")
    (directory / "scaling-results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    rows = [f"| {run['ships']} | {run['nativeProof']['nativeCompletions']} | {run['runningCards']} | "
            f"{run['faultedCards']} | {run['maxNativeLatencyTicks']} |" for run in runs]
    text = ["# Функциональные проверки масштаба Briar", "", "FULL GAME FINAL RUN NOT YET EXECUTED.", "",
            "Проверены синхронные крит/смерть/восстановление экипажа, DATA и питание ядра/шкафов.",
            "Замеры получены рядом с регрессией и не являются приёмкой производительности.",
            "Проверен один класс корпуса. Требование восьми совместимых корпусов остаётся открытым.", "",
            "| Корабли | Подтверждено воздействий | Работающих карточек | Faults | Максимальная задержка, тики |",
            "|---:|---:|---:|---:|---:|", *rows,
            "", "Все конечные очереди пусты. Сырые данные, хеши и метрики сохранены рядом с отчётом."]
    (directory / "ScalingReport.md").write_text("\n".join(text) + "\n", encoding="utf-8")
    with (directory / "scaling-summary.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=("ships", "nativeCompletions", "runningCards", "faultedCards", "maxNativeLatencyTicks"))
        writer.writeheader()
        writer.writerows(dict(ships=run["ships"], nativeCompletions=run["nativeProof"]["nativeCompletions"],
                             runningCards=run["runningCards"], faultedCards=run["faultedCards"],
                             maxNativeLatencyTicks=run["maxNativeLatencyTicks"]) for run in runs)
    html_rows = [f"<tr><td>{run['ships']}</td><td>{run['nativeProof']['nativeCompletions']}</td>"
                 f"<td>{run['runningCards']}</td><td>{run['faultedCards']}</td>"
                 f"<td>{run['maxNativeLatencyTicks']}</td></tr>" for run in runs]
    page = ('<!doctype html><meta charset="utf-8"><title>KIAS: масштаб Briar</title>'
            '<h1>Функциональные проверки масштаба Briar</h1><p>FULL GAME FINAL RUN NOT YET EXECUTED.</p>'
            '<p>Один класс корпуса. Восемь корпусов и приёмка производительности остаются открытыми.</p>'
            '<p><a href="scaling-results.json">Полные данные</a> · <a href="scaling-summary.csv">CSV</a></p>'
            '<table border="1"><tr><th>Корабли</th><th>Воздействия</th><th>Карточки</th>'
            '<th>Faults</th><th>Максимальная задержка, тики</th></tr>' + ''.join(html_rows) + '</table>')
    (directory / "scaling-report.html").write_text(page, encoding="utf-8")
    return result


def verify_scaling_recovery(final, ships):
    assert isinstance(final, dict) and {"activeGrids", "runningCards", "faultedCards", "expectedOfflineGrids", "queues"} <= set(final), "Missing final fleet telemetry"
    assert final["activeGrids"] == ships and final["runningCards"] == ships * 26, "Scaling cards not restored"
    assert final["faultedCards"] == final["expectedOfflineGrids"] == 0, "Scaling fault or offline grid"
    queues = final["queues"]
    required = {"_events", "_work", "_commands", "_boots", "_finalEvents", "_finalCommands", "pendingDispatch"}
    assert required <= set(queues) and all(type(value) is int and value == 0 for value in queues.values()), "Scaling queues did not drain"


def report(directory):
    state = read(directory / "status.json")
    assert state.get("fullGame") is True, "Not a FullGame series"
    assert [run["variant"] for run in state["completedRuns"]] == ["A", "B", "B", "A"], "Incomplete ABBA order"
    scenario = directory / "scenario.jsonl"
    inputs = lines(scenario)
    pair = read(directory / "baseline-manifest.json")
    required = set(capabilities()["required"])
    assert required <= {item["type"] for item in inputs}, "Missing active event categories"
    runs = []
    for run in state["completedRuns"]:
        path = Path(run["path"])
        fingerprints = read(path / "run-manifest.json")
        build = next(item for item in pair["builds"] if item["label"] == run["variant"])
        for name in ("assemblySha256", "nativeLabSha256", "nativeGameDriversSha256"):
            assert fingerprints[name] == build[name], f"Changed build or harness: {name}"
        proof = validate(path, scenario)
        start = read(path / "started.json")
        assert start["ships"] == state["ships"], "Unexpected ship count"
        assert start["measuredTicks"] == state["measuredTicksPerRun"], "Unexpected duration"
        assert start["warmupTicks"] == state["warmupTicks"], "Unexpected warmup"
        assert start["scenarioSha256"] == state["scenarioSha256"], "Series scenario changed"
        if run["variant"] == "B":
            final = read(path / "final-telemetry.json")
            verify_scaling_recovery(final, state["ships"])
        runs.append(dict(variant=run["variant"], path=str(path), nativeProof=proof,
                         fingerprints=fingerprints, network=validate_network(path),
                         statistics=tick_statistics(path / "ticks.csv", state["measuredTicksPerRun"]),
                         waves=wave_statistics(inputs, lines(path / "events.jsonl"),
                                               lines(path / "native-outcomes.jsonl"), state["ships"])))
    for name in ("nativeLabSha256", "nativeGameDriversSha256"):
        assert len({run["fingerprints"][name] for run in runs}) == 1, "Asymmetric native instrumentation"
    result = dict(status="NATIVE_FULLGAME_ABBA_COMPLETE", fullGameplayCoverage=False,
                  scope="Native schedule and completion evidence; network, scaling, visuals and physics acceptance require separate preflight proofs",
                  deferredChecks=capabilities()["deferredChecks"], scenarioSha256=state["scenarioSha256"], runs=runs,
                  performanceEligible=all(run["fingerprints"]["performanceEligible"] for run in runs),
                  attributionLimits="Tick/physics/driver/observer timings overlap; they must not be summed as independent costs")
    (directory / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    rows = []
    wave_rows = []
    for index, run in enumerate(runs, 1):
        ticks = run["statistics"]["wholeTick"]
        rows.append(f'<tr><td>{index} {run["variant"]}</td><td>{ticks["medianMs"]:.3f}</td>'
                    f'<td>{ticks["p99Ms"]:.3f}</td><td>{ticks["p999Ms"]:.3f}</td><td>{ticks["maxMs"]:.3f}</td>'
                    f'<td>{ticks["over60TpsBudget"]}</td></tr>')
        for wave in run["waves"]:
            latency = wave["latencyTicks"]
            wave_rows.append(f'<tr><td>{index} {run["variant"]}</td><td>{wave["tick"]}</td>'
                             f'<td>{html.escape(wave["type"])}</td><td>{wave["delivered"]}/{wave["planned"]}</td>'
                             f'<td>{max(latency) if latency else "immediate"}</td></tr>')
    deferred = ', '.join(html.escape(item["id"]) for item in result["deferredChecks"])
    page = ('<!doctype html><meta charset="utf-8"><title>KIAS Native FullGame ABBA</title>'
            '<style>body{font:16px system-ui;max-width:1100px;margin:32px auto;background:#17212b;color:#eee}'
            'td,th{padding:8px;border:1px solid #64748b}table{border-collapse:collapse}a{color:#7dd3fc}</style>'
            '<h1>Native FullGame ABBA</h1><p>Игровые результаты проверены. Итоговая готовность определяется отдельным строгим preflight.</p>'
            f'<p>Отложены, но необходимы: {deferred}.</p><p><a href="results.json">Полные доказательства, метрики и 100 самых тяжёлых тиков каждого прогона</a></p>'
            '<table><tr><th>Прогон</th><th>p50, мс</th><th>p99</th><th>p99.9</th><th>max</th><th>&gt;16.67 мс</th></tr>'
            + ''.join(rows) + '</table><h2>Волны</h2><table><tr><th>Прогон</th><th>Тик старта</th><th>Воздействие</th><th>Доставлено</th><th>Максимальная задержка, тики</th></tr>'
            + ''.join(wave_rows) + '</table>')
    (directory / "report.html").write_text(page, encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--smoke", action="store_true")
    mode.add_argument("--scaling", action="store_true")
    options = parser.parse_args()
    result = scaling_report(options.directory) if options.scaling else smoke_report(options.directory) if options.smoke else report(options.directory)
    print(json.dumps({key: result[key] for key in ("status", "performanceEligible", "scope")}))
