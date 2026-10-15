"""Validate and report a completed ABBA series without claiming full gameplay coverage."""
import argparse
import csv
import html
import json
import math
import re
import statistics
from pathlib import Path
from compare_native import compare, content_metrics, read, validate


def summarize(values):
    values = sorted(values)
    percentile = lambda fraction: values[math.ceil(len(values)*fraction)-1]
    return dict(samples=len(values), meanMs=statistics.mean(values), medianMs=statistics.median(values),
                p95Ms=percentile(.95), p99Ms=percentile(.99), p999Ms=percentile(.999), maxMs=values[-1],
                over60TpsBudget=sum(value > 1000/60 for value in values))


def system_costs(path):
    pattern = re.compile(r'^robust_entity_systems_update_usage_(sum|count)\{system="([^"]+)"\} ([^\s]+)$', re.M)
    snapshots = []
    for name in ('metrics-0.prom', 'metrics-final.prom'):
        snapshots.append({(system, field): float(value) for field, system, value in pattern.findall((path/name).read_text())})
    before, after = snapshots
    result = []
    for system in {system for system, field in after if field == 'sum'}:
        seconds = after.get((system, 'sum'), 0)-before.get((system, 'sum'), 0)
        calls = after.get((system, 'count'), 0)-before.get((system, 'count'), 0)
        if calls > 0:
            result.append(dict(system=system, updateSeconds=seconds, calls=calls, meanUpdateMs=seconds*1000/calls))
    return sorted(result, key=lambda entry: entry['updateSeconds'], reverse=True)


def validate_availability(progress, ticks=72000, ships=40):
    observed = [entry['relativeTick'] for entry in progress]
    assert observed == sorted(set(observed)), 'Repeated or unordered telemetry tick.'
    assert set(range(0, ticks, 3600)) <= set(observed), 'Missing minute snapshot.'
    assert all(0 <= tick < ticks for tick in observed)
    pending_since = None
    expected = ships * 26
    for entry in progress:
        tick, telemetry = entry['relativeTick'], entry['telemetry']
        assert telemetry['activeGrids'] == ships
        assert telemetry['activeMachines'] == expected and telemetry['faultedCards'] == 0
        if telemetry['runningCards'] != expected:
            missing = telemetry['unavailableCards']
            assert telemetry['availabilityPending'] and len(missing) == expected-telemetry['runningCards']
            assert all(card['topologyDirty'] and card['machineActive'] and not card['fault'] for card in missing)
            pending_since = tick if pending_since is None else pending_since
            assert tick-pending_since < 120, 'Persistent card unavailability.'
        elif pending_since is not None:
            assert tick-pending_since <= 120, 'Card recovery was not observed within 120 ticks.'
            pending_since = None
    assert pending_since is None, 'Pending availability was never rechecked.'


def report(directory):
    state = read(directory, 'status.json')
    runs = state['completedRuns']
    assert [run['variant'] for run in runs] == ['A', 'B', 'B', 'A']
    pairs = [compare(Path(runs[0]['path']), Path(runs[1]['path'])),
             compare(Path(runs[3]['path']), Path(runs[2]['path']))]
    assert pairs[0]['scenarioSha256'] == pairs[1]['scenarioSha256'] == state['scenarioSha256']
    data, result_runs, buckets = {'A': [], 'B': []}, [], []
    for index, run in enumerate(runs):
        path = Path(run['path'])
        start, end, events, bindings = validate(path)
        assert start['warmupTicks'] == state['warmupTicks'] and end['measuredTicks'] == state['measuredTicksPerRun']
        progress = [json.loads(line) for line in (path/'progress.jsonl').read_text().splitlines()]
        assert set(range(0, state['measuredTicksPerRun'], 3600)) <= {entry['relativeTick'] for entry in progress}
        if run['variant'] == 'B':
            validate_availability(progress, state['measuredTicksPerRun'], state['ships'])
        with (path/'ticks.csv').open(newline='') as stream:
            rows = list(csv.DictReader(stream))
        times = [float(row['whole_tick_ms']) for row in rows]
        data[run['variant']].extend(times)
        phases = {name: summarize(times[first:last]) for name, first, last in
                  [('FIRST', 0, len(times)//2), ('SECOND', len(times)//2, len(times))]}
        result_runs.append(dict(index=index+1, variant=run['variant'], path=str(path),
                                metrics=content_metrics(path), phases=phases,
                                entitySystems=system_costs(path), telemetry=progress,
                                fingerprints=read(path, 'run-manifest.json')))
        for first in range(0, len(times), 60):
            group = times[first:first+60]
            buckets.append(dict(run=index+1, variant=run['variant'], second=first//60,
                                mean=statistics.mean(group), max=max(group)))
    result = dict(status='LIMITED_WORKLOAD_ABBA_COMPLETE', fullGameplayCoverage=False,
                  order=['A', 'B', 'B', 'A'], ships=state['ships'], measuredTicksPerRun=state['measuredTicksPerRun'], nominalMinutesPerRun=state['measuredTicksPerRun']/3600,
                  warmupTicks=state['warmupTicks'], scenarioSha256=state['scenarioSha256'], parity=pairs,
                  wholeTick={label: summarize(values) for label, values in data.items()}, runs=result_runs,
                  limitations=['Seven native driver pairs only; FTL, docking, weapon fire, collisions and DATA interruption are not in this workload.',
                               'Native human crew have no registered KIAS transponders; their damage inputs do not demonstrate CrewCritical execution.',
                               'TickStart/Stop excludes game-loop frame input and frame update outside Tick.',
                               'Prometheus start scrape follows measurement tick zero; system counters cover the actual scrape interval, not exact 72000 ticks.',
                               'System Update histograms include synchronous called handlers; they do not provide complete per-method attribution.',
                               'Minute-boundary queue samples are not peak backlog measurements; whole-world runtime parity and causal latency acceptance remain incomplete.'])
    (directory/'results.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    table = ''.join(f'<tr><td>{label}</td><td>{entry["medianMs"]:.3f}</td><td>{entry["p95Ms"]:.3f}</td><td>{entry["p99Ms"]:.3f}</td><td>{entry["maxMs"]:.3f}</td><td>{entry["over60TpsBudget"]}</td></tr>' for label, entry in result['wholeTick'].items())
    limitations = ''.join(f'<li>{html.escape(value)}</li>' for value in result['limitations'])
    links = ''.join(f'<li>Запуск {index+1} ({run["variant"]}): <a href="{Path(run["path"]).as_uri()}/ticks.csv">тики CSV</a>, <a href="{Path(run["path"]).as_uri()}/events.jsonl">события JSONL</a></li>' for index, run in enumerate(runs))
    page = '''<!doctype html><meta charset="utf-8"><title>KIAS — нагрузка ABBA</title>
<style>body{background:#17212b;color:#e8edf2;font:16px system-ui;max-width:1050px;margin:32px auto}table{border-collapse:collapse}td,th{padding:10px;border:1px solid #526170}a{color:#91d5ff}canvas{background:#101820;width:100%;height:300px}select{padding:8px}</style>
<h1>40 кораблей · A–B–B–A · 20 минут на запуск</h1><p>Серия завершена. Это измерение ограниченной нагрузки из семи пар воздействий. Полное игровое покрытие не подтверждено.</p>
<table><tr><th>Сборка</th><th>p50, мс</th><th>p95</th><th>p99</th><th>Максимум</th><th>Тиков &gt; 16,67 мс</th></tr>TABLE</table>
<p>Выберите запуск. График показывает среднее и максимум за каждую секунду; все исходные пики сохранены в CSV.</p><select id="run"><option value="1">1 · A</option><option value="2">2 · B</option><option value="3">3 · B</option><option value="4">4 · A</option></select><canvas id="plot" width="1000" height="300"></canvas><p id="point"></p><h2>Raw evidence</h2><ul>LINKS</ul><h2>Границы вывода</h2><ul>LIMITS</ul>
<script>const samples=__PLOT_DATA__,c=document.getElementById('plot'),ctx=c.getContext('2d'),select=document.getElementById('run');let current=[];function draw(){current=samples.filter(p=>p.run===+select.value);ctx.clearRect(0,0,1000,300);const ceiling=Math.max(17,...current.map(p=>p.max));for(const [field,color]of [['max','#ffac73'],['mean','#71d1e2']]){ctx.beginPath();ctx.strokeStyle=color;current.forEach((p,i)=>{const x=i/Math.max(1,current.length-1)*1000,y=285-p[field]/ceiling*260;i?ctx.lineTo(x,y):ctx.moveTo(x,y)});ctx.stroke()}ctx.fillStyle='#e8edf2';ctx.fillText('мс; верхняя граница '+ceiling.toFixed(1),8,15)}select.onchange=draw;c.onmousemove=e=>{const i=Math.max(0,Math.min(current.length-1,Math.round((e.clientX-c.getBoundingClientRect().left)/c.clientWidth*Math.max(1,current.length-1)))),p=current[i];document.getElementById('point').textContent=`Секунда ${p.second}, тики ${p.second*60}–${p.second*60+59}: среднее ${p.mean.toFixed(3)} мс, максимум ${p.max.toFixed(3)} мс`};draw();</script>'''
    page = page.replace('40 кораблей · A–B–B–A · 20 минут на запуск', f"{state['ships']} кораблей · A–B–B–A · {state['measuredTicksPerRun']/3600:g} минут на запуск")
    page = page.replace('TABLE', table).replace('LINKS', links).replace('LIMITS', limitations).replace('__PLOT_DATA__', json.dumps(buckets))
    (directory/'report.html').write_text(page, encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    result = report(args.directory)
    print(json.dumps({'status': result['status'], 'wholeTick': result['wholeTick']}, indent=2))
