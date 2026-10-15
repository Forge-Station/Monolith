"""Validate dispatch and native completion evidence without certifying full coverage."""
import argparse
import hashlib
import json
import math
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def lines(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def integer(value, label, minimum=0):
    assert type(value) is int and value >= minimum, f"Invalid integer {label}"
    return value


def validate(directory, scenario):
    start, end = read(directory / "started.json"), read(directory / "completed.json")
    inputs = sorted(lines(scenario), key=lambda item: (item["tick"], item["id"]))
    events = lines(directory / "events.jsonl")
    outcomes = lines(directory / "native-outcomes.jsonl")
    duration = integer(start["measuredTicks"], "measuredTicks", 1)
    ships = integer(start["ships"], "ships", 1)
    integer(start["plannedEvents"], "plannedEvents")
    for field in ("measuredTicks", "ships", "deliveredEvents", "nativeCompleted"):
        integer(end[field], field)
    assert start["scenarioSha256"] == hashlib.sha256(scenario.read_bytes()).hexdigest(), "Scenario hash mismatch"
    assert end["status"] == "PARTIAL_NATIVE_DRY_RUN_COMPLETE", "Missing native completion"
    assert end["deliveredEvents"] == start["plannedEvents"] == len(inputs) == len(events), "Missing dispatch"
    assert end["ships"] == start["ships"] > 0, "Fleet mismatch"
    assert end["measuredTicks"] == start["measuredTicks"], "Duration mismatch"
    assert len({event["id"] for event in events}) == len(events), "Duplicate dispatch"
    assert len({outcome["id"] for outcome in outcomes}) == len(outcomes), "Duplicate native completion"
    pending = {}
    engine_ticks = {}
    engine_offset = None
    fleet = {f"ship-{index:04d}" for index in range(1, ships + 1)}
    for expected, actual in zip(inputs, events):
        tick = integer(expected["tick"], "scenario tick")
        assert tick < duration, "Dispatch outside measured window"
        assert expected["ship"] in fleet, "Unknown scenario ship"
        for field in ("scheduledTick", "actualTick", "engineTick"):
            integer(actual[field], field)
        assert (expected["id"], expected["ship"], expected["type"], expected["tick"]) == (
            actual["id"], actual["ship"], actual["type"], actual["scheduledTick"]), "Replay binding mismatch"
        assert actual["actualTick"] == expected["tick"], "Late synchronous dispatch"
        assert engine_ticks.setdefault(expected["tick"], actual["engineTick"]) == actual["engineTick"], "Same scheduled tick spans engine ticks"
        offset = actual["engineTick"] - actual["actualTick"]
        if engine_offset is None:
            engine_offset = offset
        assert offset == engine_offset and offset >= 0, "Inconsistent engine tick origin"
        assert actual["nativeEffect"], "Empty native effect"
        if actual["nativeEffect"].get("asynchronous"):
            integer(actual["nativeEffect"]["deadlineTicks"], "deadlineTicks", 1)
            pending[actual["id"]] = actual
    assert set(pending) == {outcome["id"] for outcome in outcomes}, "Missing or unexpected native completion"
    assert end["nativeCompleted"] == len(outcomes), "Native completion counter mismatch"
    transfer_platforms = {}
    requested_rotations = {entry['id']: entry['fixtureRotationDegrees'] for entry in inputs if 'fixtureRotationDegrees' in entry}
    for outcome in outcomes:
        for field in ("scheduledTick", "nativeStartedTick", "nativeCompletedTick", "relativeCompletedTick", "latencyTicks"):
            integer(outcome[field], field)
        event = pending[outcome["id"]]
        assert outcome["ship"] == event["ship"] and outcome["type"] == event["type"], "Native binding mismatch"
        assert outcome["postconditionTrue"] is True and outcome["proof"], "Missing native proof"
        if outcome['id'] in requested_rotations:
            requested = requested_rotations[outcome['id']]
            actual = outcome['proof'].get('fixtureRotationDegrees')
            assert type(requested) in (int, float) and math.isfinite(requested), 'Invalid requested rotation'
            assert type(actual) in (int, float) and math.isfinite(actual), 'Missing actual fixture rotation'
            loaded = outcome['proof'].get('requestedFixtureRotationDegrees')
            assert type(loaded) in (int, float) and math.isfinite(loaded) and abs(loaded - requested) <= .001, 'Rotation lost during native input loading'
            assert abs((actual - requested + 180) % 360 - 180) <= .001, 'Actual fixture rotation differs from requested rotation'
        topology = outcome['proof'].get('topology')
        if outcome['type'] in ('door.open', 'door.close', 'geometry.breach', 'geometry.repair') and topology and 'inputId' in topology:
            assert topology['inputId'] == outcome['id'], 'Room observation belongs to another input'
            assert type(topology['pendingAtDispatch']) is bool and type(topology['intentionalBreach']) is bool, 'Invalid room observation state'
            before = integer(topology['revisionBefore'], 'revisionBefore')
            after = integer(topology['revisionAfter'], 'revisionAfter')
            assert after >= before and topology['status'] != 'ExteriorSector' and topology['advancedStatus'] != 'ExteriorSector', 'Invalid committed room topology'
            if outcome['type'].startswith('geometry.'):
                assert after > before, 'Geometry topology revision did not advance'
            if outcome['type'].startswith('door.'):
                assert topology['usable'] or topology['intentionalBreach'], 'Unusable door room without an actual breach'
            if outcome['type'] == 'geometry.repair':
                assert topology['usable'] and topology['status'] == 'Ok' and not topology['intentionalBreach'], 'Room repair did not recover interior'
        if outcome['type'] in ('crew.depart', 'crew.return'):
            proof=outcome['proof']
            assert proof.get('nativeGridTransfer') is True and proof.get('transferPlatformRetained') is True, "Crew transfer platform removed during network transfer"
            assert proof.get('reusableTransferPlatform'), "Missing reusable crew transfer platform"
            assert transfer_platforms.setdefault(outcome['ship'],proof['reusableTransferPlatform']) == proof['reusableTransferPlatform'], "Crew transfer platform was not reused"
        if outcome["type"] in ("data.cut", "data.restore"):
            proof=outcome["proof"]
            if proof.get("baselineWithoutKias"):
                assert proof.get("nativeDevicesAbsent") is True, "False baseline DATA exemption"
            else:
                integrity=proof.get("dataIntegrity") or {}
                assert integrity.get("floorTilesUnchanged") is True, "DATA changed floor tiles"
                before=integrity.get("floorBeforeSha256", "")
                assert len(before)==64 and before==integrity.get("floorAfterSha256"), "DATA floor hash mismatch"
                assert integrity.get("oldCableEntitiesDeleted") is True, "DATA retained old cable entities"
                assert integrity.get("looseCableStacks")==0 and integrity.get("dynamicCableBodies")==0, "DATA spawned physics coils"
                assert integrity.get("addedDuplicateCableEntries")==0, "DATA introduced duplicate cable entries"
                removed=integer(integrity.get("actualRemovedCables"), "actualRemovedCables", 1)
                expected=removed if outcome["type"]=="data.restore" else 0
                assert integrity.get("actualRestoredCables")==expected, "Incomplete DATA restoration"
                if outcome["type"]=="data.restore":
                    assert integrity.get("exactCableInventoryRestored") is True, "DATA inventory mismatch"
                    assert proof.get("preservedCards")==26 and proof.get("allInitiallyOnlineDevicesRestored") is True, "DATA cards or devices not recovered"
        assert outcome["scheduledTick"] == event["scheduledTick"], "Changed native schedule"
        assert outcome["nativeStartedTick"] == event["engineTick"], "Changed native start"
        latency = outcome["nativeCompletedTick"] - outcome["nativeStartedTick"]
        assert latency == outcome["latencyTicks"] >= 0, "Invalid native latency"
        assert latency <= event["nativeEffect"]["deadlineTicks"], "Native completion exceeded deadline"
        relative = outcome["nativeCompletedTick"] - engine_offset
        assert outcome["relativeCompletedTick"] == relative, "Changed native completion clock"
        assert 0 <= relative <= duration, "Native completion outside measured window"
    log = (directory / "server.log").read_text(encoding="utf-8-sig", errors="replace")
    assert "[ERRO]" not in log and "[FATL]" not in log, "Server errors invalidate native proof"
    assert not (directory / "failed.json").exists() and not (directory / "run-failed.json").exists(), "Run marked failed"
    return dict(status="PASS", scope="Dispatch and completion integrity only; category coverage is separate",
                inputs=len(inputs), nativeCompletions=len(outcomes), types=sorted({event["type"] for event in events}),
                roomObservationsStartedPending=sum(bool((row['proof'].get('topology') or {}).get('pendingAtDispatch')) for row in outcomes),
                doorsObservedDuringBreach=sum(row['type'].startswith('door.') and bool((row['proof'].get('topology') or {}).get('intentionalBreach')) for row in outcomes))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("scenario", type=Path)
    args = parser.parse_args()
    print(json.dumps(validate(args.directory, args.scenario), indent=2))
