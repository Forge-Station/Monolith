"""Validate the limited neutral replay; never promote it to full benchmark acceptance."""
import argparse, csv, json, math, statistics
from pathlib import Path


def read(path, name):
    return json.loads((path / name).read_text(encoding='utf-8-sig'))


def validate(path):
    log = path / 'server.log'
    if log.exists():
        content = log.read_text(encoding='utf-8-sig', errors='replace')
        assert '[ERRO]' not in content and '[FATL]' not in content, 'Server errors invalidate replay evidence.'
    start = read(path, 'started.json')
    end = read(path, 'completed.json')
    assert end['status'] == 'PARTIAL_NATIVE_DRY_RUN_COMPLETE'
    assert start['tickRate'] == 60
    assert start['ships'] == end['ships'] > 0
    assert start['measuredTicks'] == end['measuredTicks'] > 0
    with (path / 'ticks.csv').open(encoding='utf-8-sig', newline='') as stream:
        ticks = list(csv.DictReader(stream))
    assert [int(row['relative_tick']) for row in ticks] == list(range(end['measuredTicks']))
    assert all(float(row['content_engine_ms']) >= 0 and int(row['allocated_bytes']) >= 0 for row in ticks)
    events = [json.loads(line) for line in (path / 'events.jsonl').read_text().splitlines()]
    assert len(events) == end['deliveredEvents'] == start.get('plannedEvents', 8)
    expected = {'fire.start': ('burning', True), 'fire.clear': ('burning', False),
                'power.loss': ('disabled', True), 'power.restore': ('disabled', False),
                'crew.critical': ('mobState', 'Critical'), 'crew.recover': ('mobState', 'Alive'),
                'light.power_off': ('on', False), 'light.power_on': ('on', True),
                'radiation.start': ('enabled', True), 'radiation.clear': ('enabled', False),
                'anomaly.start': ('active', True), 'anomaly.clear': ('active', False),
                'hull.damage': ('damaged', True), 'hull.repair': ('damaged', False)}
    declared = set(start.get('driverTypes', list(expected)[:8]))
    assert declared <= set(expected), 'Unsupported declared driver.'
    assert set(event['type'] for event in events) == declared, 'Missing declared driver.'
    if 'whole_tick_ms' in ticks[0]:
        assert all(float(row['whole_tick_ms']) >= float(row['content_engine_ms']) for row in ticks)
        assert all(float(row['gc_pause_ms']) >= 0 and int(row['whole_allocated_bytes']) >= 0 for row in ticks)
    assert len({event['id'] for event in events}) == len(events), 'Duplicate causal input ID.'
    bindings = read(path, 'bindings.json')
    ships = {binding['ship'] for binding in bindings}
    assert len(bindings) == len(ships) == start['ships']
    for event in events:
        assert event['scheduledTick'] == event['actualTick']
        assert 0 <= event['actualTick'] < end['measuredTicks']
        assert event['ship'] in ships
        field, value = expected[event['type']]
        assert event['nativeEffect'][field] == value, event
        if event['type'] == 'anomaly.start':
            assert event['nativeEffect'].get('prototype') == 'AnomalyFlesh', event
            assert event['nativeEffect'].get('excludedHazardsAbsent') is True, event
    return start, end, events, bindings


def compare(a, b):
    for path in (a, b):
        if (path / 'run-manifest.json').exists():
            assert read(path, 'run-manifest.json').get('performanceEligible', True), 'Accelerated diagnostic replay is not performance evidence.'
    sa, ea, ia, ba = validate(a)
    sb, eb, ib, bb = validate(b)
    assert sa['scenarioSha256'] == sb['scenarioSha256']
    assert sa['warmupTicks'] == sb['warmupTicks']
    assert ea['measuredTicks'] == eb['measuredTicks']
    assert ba == bb, 'Native fixture bindings differ.'
    fields = ('id', 'ship', 'type', 'scheduledTick', 'actualTick')
    assert [tuple(e[k] for k in fields) for e in ia] == [tuple(e[k] for k in fields) for e in ib]
    return {'status': 'PARTIAL_NATIVE_INPUT_AND_BINDING_PARITY_PASS', 'ships': sa['ships'],
            'measuredTicksPerVariant': ea['measuredTicks'], 'eventsPerVariant': len(ia),
            'driverTypes': sorted({event['type'] for event in ia}),
            'scenarioSha256': sa['scenarioSha256'], 'a': str(a), 'b': str(b),
            'runFingerprints': {label: read(path, 'run-manifest.json') if (path / 'run-manifest.json').exists() else None
                                for label, path in (('A', a), ('B', b))},
            'contentMetrics': {'A': content_metrics(a), 'B': content_metrics(b)},
            'scope': 'Declared native driver pairs and stable bindings; not complete gameplay workload or full runtime world parity.',
            'limitations': ['Crew actors are real minded humans without KIAS transponders; native critical/recover input parity does not validate CrewCritical graph execution.'],
            'fullBenchmark': 'NOT_RUN'}


def content_metrics(path):
    with (path / 'ticks.csv').open(encoding='utf-8-sig', newline='') as stream:
        rows = list(csv.DictReader(stream))
    times = sorted(float(row['content_engine_ms']) for row in rows)
    percentile = lambda fraction: times[max(0, math.ceil(len(times) * fraction) - 1)]
    result = {'scope': 'Content PreEngine to PostEngine',
            'samples': len(times), 'meanMs': statistics.mean(times), 'medianMs': statistics.median(times),
            'p95Ms': percentile(.95), 'p99Ms': percentile(.99), 'maxMs': times[-1],
            'over60TpsBudget': sum(value > 1000 / 60 for value in times),
            'meanAllocatedBytes': statistics.mean(int(row['allocated_bytes']) for row in rows),
            'peakWorkingSetBytes': max(int(row['working_set_bytes']) for row in rows)}
    if 'whole_tick_ms' in rows[0]:
        whole = sorted(float(row['whole_tick_ms']) for row in rows)
        result['wholeTick'] = {
            'scope': 'Robust.GameLoop TickStart to TickStop; input/frame update outside Tick excluded',
            'meanMs': statistics.mean(whole), 'medianMs': statistics.median(whole),
            'p95Ms': whole[math.ceil(len(whole) * .95)-1], 'p99Ms': whole[math.ceil(len(whole) * .99)-1],
            'p999Ms': whole[math.ceil(len(whole) * .999)-1], 'maxMs': whole[-1],
            'over60TpsBudget': sum(value > 1000/60 for value in whole),
            'meanAllocatedBytes': statistics.mean(int(row['whole_allocated_bytes']) for row in rows),
            'meanThreadAllocatedBytes': statistics.mean(int(row['thread_allocated_bytes']) for row in rows),
            'gcPauseMs': sum(float(row['gc_pause_ms']) for row in rows),
            'gcCollections': {str(generation): int(rows[-1][f'gc{generation}'])-int(rows[0][f'gc{generation}']) for generation in range(3)},
            'cpuSeconds': float(rows[-1]['cpu_seconds'])-float(rows[0]['cpu_seconds']),
        }
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('a', type=Path)
    parser.add_argument('b', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = compare(args.a, args.b)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
