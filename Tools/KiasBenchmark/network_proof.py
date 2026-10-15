"""Verify real client attachment and measured network/serialization activity."""
import argparse
import json
import math
import re
from pathlib import Path

from native_proof import read


def metric(text, name, labels=""):
    found = re.findall(r"^" + re.escape(name + labels) + r"\s+(\S+)\s*$", text, re.M)
    assert len(found) == 1, f"Missing or ambiguous metric: {name}{labels}"
    value = float(found[0])
    assert math.isfinite(value) and value >= 0, f"Invalid metric: {name}"
    return value


def continuity(history, expected):
    """Validate observed session continuity; this does not certify game completion."""
    assert history and [row["checks"] for row in history] == list(range(1,len(history)+1)), "Network history missing checks"
    first = next((index for index,row in enumerate(history) if row["admitted"]), None)
    assert first is not None, "Four clients were never admitted"
    admitted = history[first:]
    assert all(row["admitted"] for row in admitted), "Admission was reset after clients joined"
    for row in admitted:
        assert row["connected"] == row["required"] == expected, "Client lost after admission"
        assert len(row["bindings"]) == expected and all(item["connected"] and item["status"] == "InGame" for item in row["bindings"]), "Session not InGame throughout soak"
    assert all(right["engineTick"]-left["engineTick"]==60 for left,right in zip(history,history[1:])), "Missing periodic network observations"
    durations=[row['admissionElapsedSeconds'] for row in admitted]
    assert all(math.isfinite(value) and value>=0 for value in durations) and durations==sorted(durations), 'Invalid admission clock'
    seconds=durations[-1]
    return dict(checks=len(history),admittedChecks=len(admitted),continuousClients=expected,wallSeconds=seconds,
                    exceedsOriginalGuestAfkTimeout=seconds>25*60,observationIntervalTicks=60)


def validate(directory):
    manifest = read(directory / "run-manifest.json")
    started, completed = read(directory / "started.json"), read(directory / "completed.json")
    assert completed["status"] == "PARTIAL_NATIVE_DRY_RUN_COMPLETE", "Network run incomplete"
    assert completed["measuredTicks"] == started["measuredTicks"] > 0, "Network measurement incomplete"
    assert completed["ships"] == started["ships"] > 0, "Network fleet mismatch"
    assert manifest["clientSandboxEnabled"] is True, "Client sandbox disabled"
    assert not any((directory / name).exists() for name in ("failed.json", "run-failed.json")), "Network run marked failed"
    for name in ("server.log", "server-error.log"):
        log = (directory / name).read_text(encoding="utf-8-sig", errors="replace")
        assert "[ERRO]" not in log and "[FATL]" not in log, "Server errors invalidate network proof"
    expected = manifest["networkClients"]
    assert expected > 0, "No real network clients requested"
    sessions = read(directory / "network-sessions.json")
    assert sessions["required"] == sessions["connected"] == expected, "Missing real client attachment"
    bindings = sessions["bindings"]
    assert len(bindings) == len({item["user"] for item in bindings}) == expected, "Duplicate real session"
    assert len({item["ConnectionId"] for item in bindings}) == expected, "Duplicate network channel"
    assert all(item["connected"] and item["status"] == "InGame" for item in bindings), "Disconnected client"
    warnings = {}
    for item in bindings:
        for suffix in (".log", "-error.log"):
            log = (directory / (item["name"] + suffix)).read_text(encoding="utf-8-sig", errors="replace")
            assert "[ERRO]" not in log and "[FATL]" not in log, "Client errors invalidate network proof"
            warnings[item["name"] + suffix] = dict(warnings=log.count("[WARN]"), droppedSendWarnings=log.count("via Lidgren: Dropped"))
    before = (directory / "metrics-0.prom").read_text(encoding="utf-8-sig")
    after = (directory / "metrics-final.prom").read_text(encoding="utf-8-sig")
    deltas = {}
    for name in ("robust_net_sent_bytes", "robust_net_recv_bytes", "robust_net_sent_messages", "robust_net_recv_messages"):
        deltas[name] = metric(after, name) - metric(before, name)
        assert deltas[name] > 0, f"No measured traffic: {name}"
    for field in ("sum", "count"):
        name = "robust_game_state_update_usage_" + field
        labels = '{area="Serialize States"}'
        deltas["serializeStates_" + field] = metric(after, name, labels) - metric(before, name, labels)
        assert deltas["serializeStates_" + field] > 0, "No measured native state serialization"
    for name in ("robust_net_dropped", "robust_net_resent_delay", "robust_net_resent_hole"):
        deltas[name] = metric(after, name) - metric(before, name)
        assert deltas[name] >= 0, f"Network counter reset: {name}"
    continuous = None
    if manifest.get("networkHistoryRequired"):
        history = [json.loads(line) for line in (directory / "network-session-history.jsonl").read_text(encoding="utf-8-sig").splitlines() if line.strip()]
        continuous = continuity(history, expected)
    return dict(status="PASS", clients=expected, measuredTicks=completed["measuredTicks"],continuousAdmission=continuous,
                performanceEligible=manifest["performanceEligible"], measuredDeltas=deltas, bindings=bindings,
                clientWarnings=warnings,
                scope="Real session attachment, traffic and state serialization; no automated client input workload")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    print(json.dumps(validate(parser.parse_args().directory), indent=2))
