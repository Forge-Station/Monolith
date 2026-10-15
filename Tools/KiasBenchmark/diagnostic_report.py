"""Describe complete and interrupted attempts without certifying partial measurements."""
import csv
import html
import json
import math
from pathlib import Path
import sys


def read_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return {}


def summarize_run(attempt):
    directory = Path(attempt["path"])
    progress = read_json(directory / "progress.json")
    ticks = []
    malformed = 0
    try:
        with (directory / "ticks.csv").open(encoding="utf-8-sig", newline="") as stream:
            for row in csv.DictReader(stream):
                try:
                    value = float(row["whole_tick_ms"])
                    if not math.isfinite(value) or value < 0:
                        raise ValueError()
                    ticks.append(value)
                except (ValueError, TypeError, KeyError):
                    malformed += 1
    except OSError:
        pass
    ticks.sort()
    incidents = []
    try:
        for line in (directory / "log-incidents.jsonl").read_text(encoding="utf-8-sig").splitlines():
            try:
                incidents.append(json.loads(line))
            except ValueError:
                malformed += 1
    except OSError:
        pass
    log_errors=0
    first_log_error=None
    for path in sorted(directory.glob("*.log")):
        with path.open(encoding="utf-8-sig", errors="replace") as stream:
            for number, line in enumerate(stream, 1):
                if "[ERRO]" in line or "[FATL]" in line:
                    log_errors+=1
                    if first_log_error is None:
                        first_log_error=dict(path=str(path), line=number, text=line[:4096].rstrip())
    def percentile(fraction):
        return ticks[min(len(ticks)-1, int((len(ticks)-1)*fraction))] if ticks else None
    return dict(attempt, serverCompleted=(directory / "completed.json").exists(),
                lastPhase=progress.get("phase"), lastTick=progress.get("currentTick"),
                deliveredEvents=progress.get("deliveredEvents"), completedNative=progress.get("completedNative"),
                pendingNative=progress.get("pendingNative"), tickSamples=len(ticks), malformedRows=malformed,
                wholeTickMs=dict(p50=percentile(.5), p95=percentile(.95), p99=percentile(.99),
                                 maximum=max(ticks) if ticks else None),
                metricSnapshots=len(list(directory.glob("metrics-*.prom"))),
                logErrorCount=log_errors, firstLogError=first_log_error,
                logIncidentCount=len(incidents), firstIncident=incidents[0] if incidents else None,
                lastIncident=incidents[-1] if incidents else None,
                runFailure=read_json(directory / "run-failed.json"),
                nativeFailure=read_json(directory / "failed.json"),
                proofFiles={name:(directory / name).exists() for name in ("native-proof.json", "network-proof.json")})


def report(series):
    state = read_json(series / "status.json")
    runs = [summarize_run(item) for item in state.get("attemptedRuns", [])]
    # An interrupted collector may leave the active run outside attemptedRuns.
    current = state.get("currentRun")
    if current and not any(item.get("index") == current for item in runs):
        path = series.parent / "runs" / f"{current}-{state.get('currentVariant')}"
        if path.exists():
            runs.append(summarize_run(dict(index=current, variant=state.get("currentVariant"), path=str(path),
                                          valid=False, error="Collector interrupted before recording result")))
    result = dict(status=state.get("status", "UNKNOWN"), diagnostic=True, strictReady=False,
                  certification="NONE: owner waived remaining preflight; partial runs are not comparable ABBA",
                  interpretation="Per-run tick percentiles cover persisted rows only. Unequal or invalid windows must not be used to claim A/B gains. Raw metrics, events, native outcomes and logs are retained alongside this summary.",
                  attempts=runs, collectorError=state.get("error"))
    def atomic(path, text):
        temporary=path.with_suffix(path.suffix+".tmp")
        temporary.write_text(text, encoding="utf-8")
        temporary.replace(path)
    atomic(series / "diagnostic-summary.json", json.dumps(result, indent=2, ensure_ascii=False))
    lines = ["# Diagnostic ABBA", "", result["status"], "", result["certification"], "", result["interpretation"], "",
             "| Run | Result | Server completed | Last tick | Samples | p95 ms | Native completed | Errors |",
             "|---|---|---|---|---|---|---|---|"]
    for row in runs:
        lines.append(f"| {row['index']}-{row['variant']} | {'VALID_RUN' if row.get('valid') else 'INVALID / PARTIAL'} | {row['serverCompleted']} | {row['lastTick']} | {row['tickSamples']} | {row['wholeTickMs']['p95']} | {row['completedNative']} | {row['logErrorCount']} |")
        if row.get("error"):
            lines.extend(["", f"Run {row['index']}: {row['error']}", ""])
    if state.get("error"):
        lines.extend(["", "Collector failure: " + state["error"]])
    markdown="\n".join(lines)+"\n"
    atomic(series / "diagnostic-summary.md", markdown)
    atomic(series / "diagnostic-summary.html", '<!doctype html><meta charset="utf-8"><title>Diagnostic ABBA</title><pre>'+html.escape(markdown)+'</pre>')
    return result


if __name__ == "__main__":
    report(Path(sys.argv[1]).resolve())
