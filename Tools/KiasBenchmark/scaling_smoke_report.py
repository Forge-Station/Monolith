"""Combine independently validated short fleet tiers without a performance claim."""
import argparse
import json
from pathlib import Path

from fullgame_report import smoke_report


def report(directories, output):
    rows = []
    harnesses = set()
    for directory in directories:
        result = smoke_report(directory)
        assert {run["variant"] for run in result["runs"]} == {"A", "B"}, "Each tier requires A and B"
        assert len(result["runs"]) == 2 and {run["group"] for run in result["runs"]} == {"composite"}
        for run in result["runs"]:
            harnesses.add((run["fingerprints"]["nativeLabSha256"], run["fingerprints"]["nativeGameDriversSha256"]))
            final = run["finalTelemetry"]
            rows.append(dict(ships=run["ships"], variant=run["variant"],
                             delivered=run["nativeProof"]["inputs"], nativeCompletions=run["nativeProof"]["nativeCompletions"],
                             synchronousWaves=len(run["waves"]), runningCards=final["runningCards"] if final else None,
                             commandLatency=final["acceptedCommandLatency"] if final else None,
                             evidence=str(directory / "smoke-results.json")))
    assert len(harnesses) == 1, "Asymmetric fleet-tier harnesses"
    assert sorted(row["ships"] for row in rows if row["variant"] == "B") == [1, 10, 20, 40], "Missing fleet tier"
    result = dict(status="FOUR_TIER_COMPOSITE_NATIVE_SMOKE_COMPLETE", performanceEligible=False,
                  fullGameplayCoverage=False,
                  scope="Composite wave tiers only; all other 40-ship event classes, hull diversity and performance acceptance remain separate requirements",
                  runs=rows)
    output.mkdir(parents=True, exist_ok=True)
    (output / "scaling-smoke.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    text = ["# Составная волна: 1/10/20/40 кораблей", "", "FULL GAME FINAL RUN NOT YET EXECUTED.", "",
            "Проверены настоящие воздействия, их физическое завершение, одинаковый серверный тик старта и восстановление карточек/очередей.",
            "Это короткие функциональные проверки, не приёмка производительности или всех классов событий на 40 кораблях.", "",
            "| Корабли | Вариант | Доставлено | Асинхронных завершений | Работающие карточки |", "|---:|---|---:|---:|---:|"]
    text.extend(f"| {row['ships']} | {row['variant']} | {row['delivered']} | {row['nativeCompletions']} | {row['runningCards'] if row['runningCards'] is not None else '—'} |" for row in rows)
    (output / "ScalingSmoke.md").write_text("\n".join(text) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directories", nargs=4, type=Path)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    print(report(options.directories, options.output)["status"])
