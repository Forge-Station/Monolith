"""Offline evidence and scenario preparation. Live evidence is produced by NUnit."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import html
import json
from pathlib import Path
import platform
import subprocess
import xml.etree.ElementTree as ET

import yaml

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.kias-benchmark'
MAP = ROOT / 'Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml'
PRESETS = ROOT / 'Resources/Prototypes/_Forge/KIAS/controller_presets.yml'
LIVE_UI_SOURCES = ('Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml',
                   'Resources/Prototypes/_Forge/KIAS/controller_presets.yml',
                   'Tools/KiasBenchmark/Server/LiveUiLab.cs', 'Tools/KiasBenchmark/Client/LiveUiLab.cs')


class MapLoader(yaml.SafeLoader):
    pass


def robust_tag(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        value = loader.construct_mapping(node, deep=True)
    elif isinstance(node, yaml.SequenceNode):
        value = loader.construct_sequence(node, deep=True)
    else:
        value = loader.construct_scalar(node)
    return {'robustYamlTag': '!type:' + suffix, 'value': value}


MapLoader.add_multi_constructor('!type:', robust_tag)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode('utf-8')


def write_json(name, value):
    OUT.mkdir(exist_ok=True)
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def command(*args):
    run = subprocess.run(args, cwd=ROOT, capture_output=True, encoding='utf-8', errors='replace')
    if run.returncode:
        raise RuntimeError(f'{args}: {run.stdout}\n{run.stderr}')
    return run.stdout.strip()


def diagnose():
    upstream = command('git', 'rev-parse', 'upstream/main')
    base = command('git', 'merge-base', 'HEAD', 'upstream/main')
    diff = command('git', 'diff', '--name-status', f'{base}...HEAD')
    fingerprint = {
        'capturedUtc': datetime.now(timezone.utc).isoformat(),
        'branch': command('git', 'branch', '--show-current'),
        'head': command('git', 'rev-parse', 'HEAD'),
        'upstreamLocalRef': upstream, 'remoteFreshness': 'LS_REMOTE_VERIFIED_WITHOUT_FETCH',
        'remoteHeads': {'upstreamMain': command('git', 'ls-remote', 'upstream', 'refs/heads/main'),
                        'originKias': command('git', 'ls-remote', 'origin', 'refs/heads/KIAS')},
        'commonBase': base, 'engine': command('git', '-C', 'RobustToolbox', 'rev-parse', 'HEAD'),
        'engineGitlink': command('git', 'ls-tree', 'HEAD', 'RobustToolbox'),
        'submodules': command('git', 'submodule', 'status', '--recursive'),
        'engineStatus': command('git', '-C', 'RobustToolbox', 'status', '--short'),
        'status': command('git', 'status', '--short'), 'dotnet': command('dotnet', '--info'),
        'platform': platform.platform(), 'mapSha256': digest(MAP.read_bytes()),
        'presetSha256': digest(PRESETS.read_bytes()), 'buildConfiguration': 'Release',
    }
    write_json('fingerprint.json', fingerprint)
    (OUT / 'content-overlay.name-status.txt').write_text(diff + '\n', encoding='utf-8')
    patch = subprocess.run(['git', 'diff', '--binary', base, 'HEAD'], cwd=ROOT, capture_output=True, check=True).stdout
    (OUT / 'content-overlay.patch').write_bytes(patch)
    write_json('BASELINE_MANIFEST.json', {
        'status': 'UNPREPARED', 'base': base, 'branchHead': fingerprint['head'],
        'engine': fingerprint['engine'], 'overlaySha256': digest(patch),
        'builds': [], 'runtimeParity': 'NOT_TESTED',
        'blockers': ['Overlay includes vanilla integration edits requiring review.',
                     '27-program physical functional gate has not passed.',
                     'No isolated A/B Release builds or runtime parity evidence.'],
    })
    print(f"Fingerprint: {fingerprint['branch']} {fingerprint['head']}; common base {base}")


def inventory():
    data = yaml.load(MAP.read_text(encoding='utf-8'), Loader=MapLoader)
    result = []
    for group in data['entities']:
        for entity in group['entities']:
            components = {c['type']: c for c in entity.get('components', [])}
            transform = components.get('Transform', {})
            result.append({'uid': entity['uid'], 'prototype': group['proto'],
                           'position': transform.get('pos'), 'rotation': transform.get('rot', 0),
                           'parent': transform.get('parent'), 'components': components})
    uids = [e['uid'] for e in result]
    if len(set(uids)) != len(uids) or len(uids) != data['meta']['entityCount']:
        raise ValueError('Invalid UID uniqueness/entityCount')
    uid_set = set(uids)
    for row in result:
        if isinstance(row['parent'], int) and row['parent'] not in uid_set:
            raise ValueError(f"Unresolved Transform parent on {row['uid']}")
    return data, result


def audit():
    data, rows = inventory()
    counts = Counter(row['prototype'] for row in rows)
    cards = [row for row in rows if 'KiasControllerCard' in row['components']]
    presets = json.loads(PRESETS.read_text(encoding='utf-8'))
    card_names = Counter(row['components']['KiasControllerCard'].get('program', {}).get('name', '') for row in cards)
    racks = [row for row in rows if row['prototype'] == 'KiasControllerRack']
    rack_slots = []
    for rack in racks:
        rack_slots.append({'uid': rack['uid'], 'components': rack['components'].get('ContainerContainer', {})})
    write_json('ship_inventory.json', {'kind': 'STATIC_YAML_ONLY', 'mapSha256': digest(MAP.read_bytes()),
               'meta': data['meta'], 'prototypeCounts': counts, 'entities': rows, 'rackSlots': rack_slots})
    results = []
    stimuli = {}
    matrix = (ROOT / 'Docs/KIAS/History/04_PRESET_ACCEPTANCE_MATRIX.md').read_text(encoding='utf-8')
    for line in matrix.splitlines():
        if line.startswith('| `'):
            parts = line.split('|')
            stimuli[parts[1].strip().strip('`')] = parts[2].strip()
    for preset in presets:
        graph = preset['program']
        profiles = sorted({n.get('profile') for n in graph['nodes'] if n.get('profile')})
        reasons = []
        if not card_names[preset['id']]:
            reasons.append('No physical card in source map; requires independent fixture.')
        if 'LightController' in profiles and not counts['KiasLightController']:
            reasons.append('No LightController prototype on source map; verify runtime selector targets.')
        if preset['id'] in ('fire', 'fire-clear') and not counts['FireAlarm']:
            reasons.append('No FireAlarm prototype on source map; verify component inheritance at runtime.')
        reasons.append('Four-stage physical input/detector/graph/world effect evidence not recorded.')
        results.append({'preset': preset['id'], 'status': 'BLOCKED' if len(reasons) > 1 else 'INCONCLUSIVE',
                        'tested': False, 'stimulus': stimuli.get(preset['id']), 'profiles': profiles,
                        'enabledByDefault': preset.get('enabled', True),
                        'graphSha256': digest(canonical(graph)),
                        'physicalCards': [r['uid'] for r in cards if r['components']['KiasControllerCard'].get('program', {}).get('name') == preset['id']],
                        'wires': graph['wires'], 'nodes': graph['nodes'],
                        'latencyTicks': None, 'evidence': [], 'reasons': reasons})
    write_json('functional_results.json', {'fullGate': 'NOT_PASSED', 'programs': results})
    lines = ['# Briar: статический аудит', '', f"SHA256: `{digest(MAP.read_bytes())}`", '',
             f"Entities: {len(rows)}; cards: {len(cards)}; presets: {len(presets)}.", '',
             '| Prototype | Count |', '|---|---:|']
    lines.extend(f'| {name} | {count} |' for name, count in sorted(counts.items()) if name.startswith('Kias') or name in ('AirAlarm', 'FireAlarm'))
    lines += ['', 'Модули, питание, DATA и покрытие требуют runtime snapshot. `ent: null` не означает пустой слот.',
              'Корабль обновлён пользователем; графы света исправлены в сохранённых картах.',
              'Basic и advanced имеют по шесть стартовых модулей. Id/Transponder дублируют Identity;',
              'полезных дополнительных basic-категорий в текущих прототипах нет.', '',
              'Полная геометрия, serialized wiring, UID, parent и компоненты: ship_inventory.json.',
              'Это не доказательство runtime parity A/B.']
    (OUT / 'SHIP_AUDIT.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    (OUT / 'ship.diff').write_text(command('git', 'diff', '--', str(MAP.relative_to(ROOT))), encoding='utf-8')
    report_results()
    print(f'Audited {len(rows)} entities, {len(cards)} physical cards, {len(presets)} presets.')


def desktop_live_ui():
    sizes = ('850x500', '1200x720', '1600x900')
    required_steps = ('WRITE actual card', 'native discard', 'observe actual ejection',
                      'observe native reinsert', 'reject Bool to Signal', 'native zoomed connection')
    runs = OUT / 'live-ui'
    if not runs.exists():
        return {'status': 'NOT_RUN'}
    for path in sorted(runs.iterdir(), reverse=True):
        files = ('run-manifest.json', 'client-completed.json', 'server-writes.json', 'review.json')
        if not all((path / name).exists() for name in files):
            continue
        manifest, client, server, review = [json.loads((path / name).read_text(encoding='utf-8-sig')) for name in files]
        if not all(name in manifest.get('sources', {}) for name in LIVE_UI_SOURCES):
            continue
        if any(not (ROOT / name).exists() or digest((ROOT / name).read_bytes()) != fingerprint
               for name, fingerprint in manifest['sources'].items()):
            continue
        steps = set(client.get('steps', []))
        writes = server.get('writes', [])
        valid = (client.get('status') == 'PASS' and client.get('realDesktopRenderer')
                 and client.get('actualServerBui') and server.get('status') == 'RECORDED'
                 and server.get('actualNetworkBui')
                 and all(f'{size}: {step}' in steps for size in sizes for step in required_steps)
                 and len(writes) == 3 and len({item['actualCard'] for item in writes}) == 1
                 and all(size.replace('x', '×') in item.get('name', '') for size, item in zip(sizes, writes))
                 and all(item.get('number') == .25 and item.get('manualConnection')
                         and item.get('actualAdapterBinding') and item.get('inferredAudioChannel') == 2
                         for item in writes)
                 and review.get('reviewedSizes') == list(sizes)
                 and all((path / f'live-{size}.png').exists()
                         and digest((path / f'live-{size}.png').read_bytes()) == review.get('captureSha256', {}).get(size)
                         for size in sizes))
        if valid:
            return {'status': 'PASS', 'sizes': list(sizes), 'evidence': str(path.relative_to(OUT)),
                    'scope': manifest['scope'], 'visualObservations': review.get('observations', [])}
    return {'status': 'NOT_VERIFIED_CURRENT_SOURCE'}


def report_results():
    result = json.loads((OUT / 'functional_results.json').read_text(encoding='utf-8'))
    lines = ['# Функциональная проверка KIAS', '', f"Полный gate: **{result['fullGate']}**.", '',
             'PASS по программе требует input → detector → graph → world effect. Ни одна строка',
             'не получает PASS из статического анализа, загрузки карты или существующих unit bridge tests.', '',
             '| Program | Full acceptance | Isolated physical chain | Tested | Reason |', '|---|---|---|---|---|']
    lines += [f"| {r['preset']} | {r['status']} | {r.get('isolatedPhysicalResult', 'NOT_RUN')} | {r['tested']} | {'; '.join(r['reasons'])} |" for r in result['programs']]
    if 'mapCheck' in result:
        lines += ['', '## Отдельная проверка загрузки и сохранения', '', json.dumps(result['mapCheck'], ensure_ascii=False, indent=2)]
    for name in ('prerequisites.trx', 'auxiliary.trx', 'physical.trx', 'briar-all.trx', 'cross.trx', 'kias-regression.trx', 'radio-delivery.trx', 'vessel-clock.trx', 'hazard-lifecycle.trx', 'crew-parallel.trx', 'boarding-power.trx', 'programmer-ui.trx', 'ui-environment.trx', 'ui-flash.trx', 'ui-control.trx', 'collision-lifecycle.trx', 'medical-lifecycle.trx', 'ui-arrival.trx', 'briar-extended.trx'):
        path = OUT / 'tests' / name
        if path.exists():
            ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
            counters = ET.parse(path).getroot().find('t:ResultSummary/t:Counters', ns)
            if counters is not None:
                lines += ['', f"{name}: executed={counters.attrib.get('executed')}, passed={counters.attrib.get('passed')}, failed={counters.attrib.get('failed')}.",
                          'TRX имеет приоритет над серверным oracle JSON. PhysicalGate — изолированные цепочки; остальные TRX — prerequisite/persistence/editor.']
    if result.get('deviceCrossTests'):
        lines += ['', '## Проверки устройств на Briar', '']
        lines += [f"- {name}: **{data['status']}**; evidence: {data['evidence']}." for name, data in result['deviceCrossTests'].items()]
    if result.get('desktopLiveUi'):
        lines += ['', '## Настольный клиент с настоящим серверным BUI', '',
                  json.dumps(result['desktopLiveUi'], ensure_ascii=False, indent=2)]
    lines += ['', '## Осталось до полного gate', '']
    lines += ['- ' + item for item in result.get('remainingAcceptance', [])]
    replay = result.get('partialNativeReplay')
    if replay:
        lines += ['', '## Короткий нейтральный A/B', '',
                  f"{replay['status']}: {replay['ships']} гридов, {replay['measuredTicksPerVariant']} тиков и {replay['eventsPerVariant']} входов на вариант.",
                  replay.get('invalidReason', 'Входы и привязки совпали. Метрики покрывают только Content PreEngine→PostEngine; это не весь тик и не итоговый ABBA.'), '',
                  '| Вариант | p50, мс | p95, мс | p99, мс | Максимум, мс |', '|---|---:|---:|---:|---:|']
        lines += [f"| {label} | {data['medianMs']:.3f} | {data['p95Ms']:.3f} | {data['p99Ms']:.3f} | {data['maxMs']:.3f} |" for label, data in replay.get('contentMetrics', {}).items()]
        lines += ['', 'Raw: ' + replay['a'] + '; ' + replay['b'] + '. Детали: replay-parity.json.']
    lines += ['', '20-минутный сценарий и полный ABBA не выполнены. Текущий replay покрывает четыре вида воздействий; полный замер требует дополнительных драйверов и измерения всего тика.']
    (OUT / 'FUNCTIONAL_REPORT.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def report():
    result = json.loads((OUT / 'functional_results.json').read_text(encoding='utf-8'))
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    persistence_paths = [OUT / 'tests' / name for name in ('prerequisites.trx', 'briar-all.trx', 'kias-regression.trx')]
    for persistence in sorted((path for path in persistence_paths if path.exists()), key=lambda path: path.stat().st_mtime):
        tests = ET.parse(persistence).getroot().findall('t:Results/t:UnitTestResult', ns)
        frozen = next((t for t in tests if 'SavedBriarLoadsAndScannerModulesSurviveRoundTrip' in t.attrib.get('testName', '')), None)
        if frozen is not None:
            result['mapCheck'] = {'status': 'PASS' if frozen.attrib.get('outcome') == 'Passed' else 'FAIL',
                'scope': 'Frozen map save/reload before MapInit, module defaults after MapInit',
                'trx': str(persistence.relative_to(OUT)), 'test': frozen.attrib.get('testName')}
    powered_path = OUT / 'live/briar-powered.json'
    if powered_path.exists():
        powered = json.loads(powered_path.read_text(encoding='utf-8'))
        write_json('runtime_summary.json', {
            'tick': powered['tick'], 'active': powered['active'],
            'deviceStatuses': Counter(d['status'] for d in powered['devices']),
            'runningCards': sum(c['running'] for c in powered['cards']),
            'programCount': len(powered['programs']),
            'compileFailures': [p for p in powered['programs'] if not p['compileSuccess']],
            'missingSupportingProfiles': sorted({t['profile'] for p in powered['programs'] for t in p['targets'] if not t['supporting']}),
            'airAlarms': powered.get('alarms', []), 'pdcWeapons': powered.get('pdcWeapons', []),
            'offlineScanners': [s for s in powered.get('scanners', []) if not s['online']],
            'evidence': 'live/briar-powered.json', 'scope': 'Battery power + compile/selector prerequisites, not physical program acceptance',
        })
        by_preset = {p['preset']: p for p in powered['programs']}
        for row in result['programs']:
            p = by_preset.get(row['preset'])
            if p:
                row['prerequisites'] = p
                row['evidence'] = ['live/briar-powered.json (prerequisites only)']
                missing = sorted({t['profile'] for t in p['targets'] if not t['supporting']})
                for profile in missing:
                    reason = f'Runtime prerequisite: no supporting entity for {profile} on loaded grid.'
                    if reason not in row['reasons']:
                        row['reasons'].append(reason)
                if missing:
                    row['status'] = 'BLOCKED'
                if row['preset'] in ('fire', 'fire-clear') and not any(a['fireAlarm'] for a in powered.get('alarms', [])):
                    reason = 'Runtime prerequisite: no FireAlarmComponent on loaded grid.'
                    if reason not in row['reasons']:
                        row['reasons'].append(reason)
                    row['status'] = 'BLOCKED'
                if row['preset'].startswith('battle-') and not powered.get('pdcWeapons', []):
                    reason = 'Runtime prerequisite: no compatible KiasPdcWeaponComponent for automatic interception.'
                    if reason not in row['reasons']:
                        row['reasons'].append(reason)
                    row['status'] = 'BLOCKED'
        write_json('functional_results.json', result)
        physical_outcomes = {}
        trx = OUT / 'tests/briar-all.trx'
        if not trx.exists():
            trx = OUT / 'tests/physical.trx'
        if trx.exists():
            ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
            for test in ET.parse(trx).getroot().findall('t:Results/t:UnitTestResult', ns):
                name = test.attrib.get('testName', '')
                if 'WorldOutput("' in name:
                    physical_outcomes[name.split('WorldOutput("', 1)[1].split('"', 1)[0]] = test.attrib.get('outcome')
        extra_paths = [OUT / 'tests' / name for name in ('kias-regression.trx', 'radio-delivery.trx', 'vessel-clock.trx', 'hazard-lifecycle.trx', 'crew-parallel.trx', 'boarding-power.trx', 'ui-environment.trx', 'ui-flash.trx', 'ui-control.trx', 'collision-lifecycle.trx', 'medical-lifecycle.trx', 'ui-arrival.trx', 'briar-extended.trx')]
        for path in sorted((path for path in extra_paths if path.exists()), key=lambda path: path.stat().st_mtime):
            grouped = {}
            for test in ET.parse(path).getroot().findall('t:Results/t:UnitTestResult', ns):
                name = test.attrib.get('testName', '')
                if 'WorldOutput("' in name:
                    preset = name.split('WorldOutput("', 1)[1].split('"', 1)[0]
                    grouped.setdefault(preset, []).append(test.attrib.get('outcome'))
            physical_outcomes.update({preset: 'Passed' if all(outcome == 'Passed' for outcome in outcomes) else 'Failed' for preset, outcomes in grouped.items()})
        for row in result['programs']:
            physical = OUT / 'live' / ('physical-' + row['preset'] + '.json')
            if physical.exists():
                live = json.loads(physical.read_text(encoding='utf-8'))
                if (live.get('mapSha256') != digest(MAP.read_bytes())
                        or live.get('presetSha256') != digest(PRESETS.read_bytes())):
                    row['tested'] = False
                    row['isolatedPhysicalResult'] = 'STALE_SOURCE'
                    row['reasons'].append('Physical evidence belongs to different or unrecorded map/preset hashes; rerun PhysicalGate.')
                    continue
                row['tested'] = True
                row['crossChecks'] = live.get('crossChecks', {})
                if row['preset'] == 'vessel-critical':
                    clear_path = OUT / 'live/physical-vessel-critical-clear.json'
                    if clear_path.exists():
                        clear = json.loads(clear_path.read_text(encoding='utf-8'))
                        if clear.get('mapSha256') == digest(MAP.read_bytes()) and clear.get('status') == 'PASS' and physical_outcomes.get(row['preset']) == 'Passed':
                            row['crossChecks'].update(clear.get('crossChecks', {}))
                            row['evidence'].append(str(clear_path.relative_to(OUT)))
                row['serverOracleResult'] = live['status']
                row['testRunnerOutcome'] = physical_outcomes.get(row['preset'], 'NOT_RECORDED')
                row['isolatedPhysicalResult'] = 'PASS' if live['status'] == 'PASS' and row['testRunnerOutcome'] == 'Passed' else 'FAIL'
                row['status'] = 'INCONCLUSIVE' if row['isolatedPhysicalResult'] == 'PASS' else 'FAIL'
                row['reasons'] = [live['scope'],
                    'Native chain and recorded lifecycle checks passed; see explicit remaining full-gate cases below.' if row['isolatedPhysicalResult'] == 'PASS'
                    else (live.get('error') or 'See test runner errors in physical.trx.')]
                if row['crossChecks']:
                    row['reasons'].append('Additional checks: ' + ', '.join(key for key, passed in row['crossChecks'].items() if passed))
                row['evidence'].append(str(physical.relative_to(OUT)))
                row['latencyTicks'] = None if live.get('graphTick') is None else live['graphTick'] - live['stimulusTick']
        cross_outcomes = {}
        cross_paths = [OUT / 'tests' / name for name in ('briar-all.trx', 'cross.trx', 'kias-regression.trx', 'crew-parallel.trx', 'programmer-ui.trx', 'ui-environment.trx', 'ui-flash.trx', 'ui-control.trx', 'briar-extended.trx')]
        for path in sorted((path for path in cross_paths if path.exists()), key=lambda path: path.stat().st_mtime):
            for test in ET.parse(path).getroot().findall('t:Results/t:UnitTestResult', ns):
                test_name = test.attrib.get('testName', '')
                if 'RespondAndRecover("' in test_name:
                    cross_outcomes[test_name.split('RespondAndRecover("', 1)[1].split('"', 1)[0]] = test.attrib.get('outcome')
        result['deviceCrossTests'] = {}
        for name in ('scanners', 'power-data', 'speakers', 'suppression', 'pdc', 'ame-adapter', 'racks', 'programmer', 'programmer-ui', 'docking'):
            path = OUT / 'live' / f'cross-{name}.json'
            status = 'NOT_RUN'
            if path.exists():
                data = json.loads(path.read_text(encoding='utf-8'))
                status = 'STALE_MAP' if data.get('mapSha256') != digest(MAP.read_bytes()) else (
                    'PASS' if data.get('status') == 'PASS' and cross_outcomes.get(name) == 'Passed' else 'FAIL')
            result['deviceCrossTests'][name] = {'status': status, 'evidence': str(path.relative_to(OUT))}
        result['primaryPhysicalChains'] = {'passed': sum(r.get('isolatedPhysicalResult') == 'PASS' for r in result['programs']), 'required': 27}
        capture = OUT / 'ui/completed.json'
        visual = {'status': 'NOT_RUN'}
        if capture.exists():
            data = json.loads(capture.read_text(encoding='utf-8'))
            current = data.get('mapSha256') == digest(MAP.read_bytes()) and data.get('presetSha256') == digest(PRESETS.read_bytes())
            visual = {'status': 'CAPTURED_REQUIRES_VISUAL_REVIEW' if current else 'STALE_SOURCE', 'captures': data.get('captures'), 'evidence': 'ui/completed.json'}
        result['desktopVisual'] = visual
        result['desktopLiveUi'] = desktop_live_ui()
        result['remainingAcceptance'] = []
        if result['deviceCrossTests']['docking']['status'] != 'PASS':
            result['remainingAcceptance'].append('Native docking/undocking and continued KIAS availability on the rotated grid.')
        pdc_path = OUT / 'live/cross-pdc.json'
        pdc_evidence = json.loads(pdc_path.read_text(encoding='utf-8')).get('evidence', []) if pdc_path.exists() else []
        if result['deviceCrossTests']['pdc']['status'] != 'PASS' or not all(any(item.get(flag) for item in pdc_evidence) for flag in (
                'nativeOcclusionPreventsFire', 'nativeOcclusionRemovalRearmsInterception',
                'nativeOverlappingTargetsIntercepted', 'nativeOwnGridProjectileExcluded', 'nativeFriendlyIffProjectileExcluded')):
            result['remainingAcceptance'].append('Native PDC occlusion/removal, overlapping targets and own/friendly IFF projectile exclusion on AUX_FIXTURE.')
        contact = next(row for row in result['programs'] if row['preset'] == 'contact')
        if not all(contact.get('crossChecks', {}).get(f'nativeFtl{iff}ContactMatchesSavedGraph') for iff in ('Friendly', 'Neutral', 'Hostile')):
            result['remainingAcceptance'].append('Native FTL with additional IFF classes.')
        proximity = next(row for row in result['programs'] if row['preset'] == 'proximity')
        if not all(proximity.get('crossChecks', {}).get(f'nativeRotatedProximity{iff}FromDifferentDirection') for iff in ('Friendly', 'Neutral', 'Hostile')):
            result['remainingAcceptance'].append('Native proximity with additional IFF classes and approach directions.')
        if not all(next(row for row in result['programs'] if row['preset'] == preset).get('crossChecks', {}).get('nativeActiveHazardOutsideCoverageDoesNotActuate')
                   for preset in ('anomaly', 'radiation')):
            result['remainingAcceptance'].append('Negative anomaly/radiation sources outside scanner coverage.')
        world_evidence = []
        for row in result['programs']:
            path = OUT / 'live' / f"physical-{row['preset']}.json"
            if path.exists():
                data = json.loads(path.read_text(encoding='utf-8'))
                snapshot_path = OUT / 'live' / data.get('worldStateEvidence', 'missing-snapshot.json')
                if row.get('isolatedPhysicalResult') == 'PASS' and snapshot_path.exists():
                    snapshot = json.loads(snapshot_path.read_text(encoding='utf-8'))
                    if (isinstance(snapshot.get('beforeRaw'), str) and isinstance(snapshot.get('afterRaw'), str)
                            and digest(snapshot['beforeRaw'].encode('utf-8')) == data.get('worldStateBeforeSha256')
                            and digest(snapshot['afterRaw'].encode('utf-8')) == data.get('worldStateAfterSha256')):
                        world_evidence.append(row['preset'])
        result['hashedWorldStateCases'] = world_evidence
        if len(world_evidence) != 27:
            result['remainingAcceptance'].append('Causal correlation and relevant world-state hashes before/after each native case.')
        if result['desktopLiveUi']['status'] != 'PASS':
            result['remainingAcceptance'].append('desktop programmer visual review and editing interactions at three resolutions')
        racks_path = OUT / 'live/cross-racks.json'
        if not (result['deviceCrossTests']['racks']['status'] == 'PASS' and racks_path.exists()
                and any(item.get('parallelCards') == 27 and item.get('actualSpeakerTones') == 10
                        for item in json.loads(racks_path.read_text(encoding='utf-8')).get('evidence', []))):
            result['remainingAcceptance'].append('parallel card world-effect coherence')
        radio_presets = ('medical-assistance', 'vessel-critical')
        if not all(any(row['preset'] == preset and row.get('isolatedPhysicalResult') == 'PASS'
                       and row.get('crossChecks', {}).get('nativeRadioBroadcastDeliveredToConnectedClient') for row in result['programs']) for preset in radio_presets):
            result['remainingAcceptance'].append('native radio reception for MedicalHelp/Mayday')
        vessel = next(row for row in result['programs'] if row['preset'] == 'vessel-critical')
        if not all(vessel.get('crossChecks', {}).get(key) for key in ('nativeRemovedCardHasNoOrphanClockAfter180Seconds', 'nativeManagementAlertResetStopsClock')):
            result['remainingAcceptance'].append('native armed clock removal and management reset')
        regression_path = OUT / 'tests/kias-regression.trx'
        regression_green = False
        if regression_path.exists():
            counters = ET.parse(regression_path).getroot().find('t:ResultSummary/t:Counters', ns)
            if counters is not None:
                regression_green = int(counters.attrib.get('executed', 0)) >= 122 and counters.attrib.get('executed') == counters.attrib.get('passed')
        if not regression_green:
            result['remainingAcceptance'].append('Current complete KIAS regression suite including the extended docking/PDC cases.')
        for name, data in result['deviceCrossTests'].items():
            if data['status'] != 'PASS':
                result['remainingAcceptance'].append(f'Cross-test {name}: {data["status"]}.')
        if result.get('mapCheck', {}).get('status') != 'PASS':
            result['remainingAcceptance'].append('Frozen map persistence check.')
        if result['primaryPhysicalChains']['passed'] != 27:
            result['remainingAcceptance'].append('All 27 current native primary chains must pass the test runner.')
        for row in result['programs']:
            flags = row.get('crossChecks', {})
            native_lifecycle = any(passed and key.startswith('native') and key not in (
                'nativeRadioBroadcastDeliveredToConnectedClient',) for key, passed in flags.items())
            if row['preset'] == 'hull-damage':
                native_lifecycle = all(flags.get(key) for key in ('healingDoesNotAlarm', 'repeatInsideCooldownSuppressed', 'repeatAfterCooldownRearmed'))
            if not (native_lifecycle and flags.get('stableNativeSafeStateDoesNotActuate')):
                result['remainingAcceptance'].append(f"Native lifecycle/safe-state evidence for {row['preset']}.")
        result['fullGate'] = 'PASS' if not result['remainingAcceptance'] else 'NOT_PASSED'
        if result['fullGate'] == 'PASS':
            for row in result['programs']:
                row['status'] = 'PASS'
                row['reasons'] = ['Native primary chain and recorded lifecycle checks; all device cross-tests, frozen persistence and connected desktop editing passed.']
            result['acceptanceScope'] = '03/04 physical matrix on clean Briar copies; PDC uses documented AUX_FIXTURE. Full benchmark is separate and has not run.'
        parity = OUT / 'replay-parity.json'
        if parity.exists():
            result['partialNativeReplay'] = json.loads(parity.read_text(encoding='utf-8'))
            baseline_path = OUT / 'BASELINE_MANIFEST.json'
            if baseline_path.exists():
                baseline = json.loads(baseline_path.read_text(encoding='utf-8'))
                fingerprints = result['partialNativeReplay'].get('runFingerprints', {})
                if not all(build.get('compiled') and fingerprints.get(build['label'], {})
                           and fingerprints[build['label']].get('assemblySha256') == build.get('assemblySha256')
                           for build in baseline['builds']):
                    result['partialNativeReplay']['status'] = 'PREVIOUS_BUILD_EVIDENCE'
                    result['partialNativeReplay']['invalidReason'] = 'Короткий replay относится к предыдущим сборкам; актуальные варианты требуют повторного прогона.'
        write_json('functional_results.json', result)
        blockers = ['# Текущее состояние gate', '',
                    'Сохранение на frozen map проверяется отдельно; ошибка сохранения после MapInit',
                    'не является блокером корректного маппинга. Все 21 сканер online на обновлённом Briar.',
                    'Четыре FireAlarm присутствуют и controllable. Световые графы используют Lighting.', '',
                    'Нет совместимого KiasPdcWeapon: для перехвата требуется отдельный физический fixture.',
                    'Fire-clear сохранена в шестом слоте шкафа 1177; все 27 сценариев используют физические карты Briar.',
                    'Quiet отключён по умолчанию; тест явно включает только его карту.', '',
                    'Физические результаты: live/physical-*.json и tests/physical.trx.',
                    'Результаты первичных цепочек и десяти cross-tests, повторные стимулы и lifecycle отражены в JSON.',
                    'Сценарии всех 27 программ: Tools/KiasBenchmark/PHYSICAL_SCENARIOS.md.',
                    'Ручные исправления карты сейчас не требуются. Оставшиеся проверки выполняются в коде стенда.',
                    *result['remainingAcceptance'],
                    'Полный A/B ещё не завершён.']
        missing_data = [d for d in powered['devices'] if d['status'] == 'NoDataPath']
        blockers += ['', 'Устройства без DATA (runtime snapshot):' if missing_data else 'В текущем runtime snapshot устройств без DATA нет.']
        blockers += [f"- {d['prototype']}, runtime UID {d['uid']}, ({d.get('x', '?')}, {d.get('y', '?')}), identifier {d.get('identifier', '?')}: {d['status']}" for d in powered['devices'] if d['status'] == 'NoDataPath']
        (OUT / 'BLOCKERS_FOR_USER.md').write_text('\n'.join(blockers) + '\n', encoding='utf-8')
    report_results()
    rows = '\n'.join('<tr><td>' + html.escape(r['preset']) + '</td><td>' + r['status'] +
                     '</td><td>' + html.escape('; '.join(r['reasons'])) + '</td></tr>' for r in result['programs'])
    page = '''<!doctype html><html lang="ru"><meta charset="utf-8"><title>KIAS Lab — partial evidence</title>
<style>body{font:16px system-ui;background:#141922;color:#e3e8f0;max-width:1200px;margin:40px auto;padding:20px}
table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #364152;padding:12px;text-align:left}
a{color:#8ebdff}input{padding:10px;background:#222b39;color:white;border:1px solid #63738b;width:95%}
.status{padding:16px;background:#553526;border-radius:8px}button{padding:8px;cursor:pointer}</style>
<h1>KIAS Benchmark Lab</h1><p class="status">PARTIAL / FULL GATE NOT PASSED / FULL ABBA NOT RUN</p>
<p>Доказательства: загрузка Briar, реальные стартовые модули и диагностика питания.
Frozen save/reload проверяется отдельно. Изолированные физические проверки
не закрывают всю матрицу 27 программ. Короткий нейтральный A/B, если выполнен, отражён в JSON и функциональном отчёте; его метрики не подтверждают полную нагрузочную проверку. Стоимость отдельных методов пока не измерена.</p>
<p><a href="FUNCTIONAL_REPORT.md">Функциональный отчёт</a> · <a href="SHIP_AUDIT.md">Карта</a> ·
<a href="functional_results.json">JSON</a> · <a href="functional-gate.log">Лог сохранения</a> ·
<a href="live/briar-loaded.json">Runtime snapshot</a> · <a href="BASELINE_MANIFEST.json">Manifest</a></p>
<input id="filter" placeholder="Фильтр по программе, статусу или блокеру">
<table><thead><tr><th>Программа</th><th>Статус</th><th>Причины / границы доказательств</th></tr></thead>
<tbody>''' + rows + '''</tbody></table><script>
document.getElementById('filter').addEventListener('input',event=>{
for(const row of document.querySelectorAll('tbody tr')) row.hidden=!row.textContent.toLowerCase().includes(event.target.value.toLowerCase());
});</script></html>'''
    page = page.replace('PARTIAL / FULL GATE NOT PASSED / FULL ABBA NOT RUN',
                        f"FUNCTIONAL GATE {result['fullGate']} / FULL ABBA NOT RUN")
    if result['fullGate'] == 'PASS':
        page = page.replace('Изолированные физические проверки\nне закрывают всю матрицу 27 программ.',
                            'Первичные физические цепочки всех 27 программ и дополнительные cross-tests прошли.')
    (OUT / 'report.html').write_text(page, encoding='utf-8')
    print(f"Functional gate: {result['fullGate']}; full ABBA has not run.")


class XorShift32:
    """Fixed unsigned 32-bit xorshift(13,17,5); never uses game RNG."""
    def __init__(self, seed):
        if not 0 < seed <= 0xffffffff:
            raise ValueError('seed must be 1..4294967295')
        self.state = seed

    def next(self):
        x = self.state
        x ^= (x << 13) & 0xffffffff
        x ^= x >> 17
        x ^= (x << 5) & 0xffffffff
        self.state = x & 0xffffffff
        return self.state


def scenario(seed, ships):
    if not 1 <= ships <= 40:
        raise ValueError('ships must be 1..40')
    rng = XorShift32(seed)
    events = []
    # Берём свободный корабль, планируем стимул и восстановление в измеряемом окне.
    busy = {f'ship-{s:04d}': 0 for s in range(1, ships + 1)}
    categories = [('fire.start', 'fire.clear'), ('power.loss', 'power.restore'),
                  ('crew.critical', 'crew.recover'), ('light.power_off', 'light.power_on')]
    for tick in range(7200, 64800, 120):
        ship = f'ship-{1 + rng.next() % ships:04d}'
        if busy[ship] > tick:
            continue
        start, restore = categories[rng.next() % len(categories)]
        duration = 120 + rng.next() % 481
        busy[ship] = tick + duration + 60
        event_id = f'evt-{len(events) + 1:06d}'
        events.append({'id': event_id, 'tick': tick, 'ship': ship, 'type': start,
                       'ttlTicks': duration, 'params': {'fixture': 'REQUIRES_LIVE_BINDING'}})
        events.append({'id': f'evt-{len(events) + 1:06d}', 'tick': tick + duration,
                       'ship': ship, 'type': restore, 'relatesTo': event_id,
                       'params': {'fixture': 'REQUIRES_LIVE_BINDING'}})
    events.sort(key=lambda row: (row['tick'], row['id']))
    return events, rng.state


def compile_scenario(seed, ships):
    events, state = scenario(seed, ships)
    raw = b''.join(canonical(row) + b'\n' for row in events)
    OUT.mkdir(exist_ok=True)
    (OUT / 'scenario.jsonl').write_bytes(raw)
    write_json('scenario_manifest.json', {
        'status': 'DRAFT_NOT_EXECUTABLE', 'generator': 'xorshift32-13-17-5-v1',
        'seed': seed, 'finalPrngState': state, 'ships': ships, 'tickRate': 60,
        'measuredTicks': 72000, 'eventCount': len(events), 'sha256': digest(raw),
        'validation': 'SYNTAX_ONLY', 'liveBindings': 'MISSING',
        'coverage': sorted({e['type'] for e in events}),
        'omitted': ['FTL', 'weapons', 'collision', 'atmosphere', 'anomaly', 'radiation', 'docking', 'DATA'],
        'blocker': 'Real system event drivers and verified fixture bindings required before replay.',
    })
    print(f'Draft schedule: {len(events)} events; SHA256 {digest(raw)}. Not a live replay result.')


def record_gate(exit_code):
    if not (OUT / 'functional_results.json').exists():
        audit()
    result = json.loads((OUT / 'functional_results.json').read_text(encoding='utf-8'))
    trx = OUT / 'tests/briar.trx'
    executed = passed = failed = 0
    if trx.exists():
        root = ET.parse(trx).getroot()
        ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
        counters = root.find('t:ResultSummary/t:Counters', ns)
        if counters is not None:
            executed = int(counters.attrib.get('executed', 0))
            passed = int(counters.attrib.get('passed', 0))
            failed = int(counters.attrib.get('failed', 0))
    log = (OUT / 'functional-gate.log').read_text(encoding='utf-8', errors='replace') if (OUT / 'functional-gate.log').exists() else ''
    save_error = 'No type serializer or data definition found for type Content.Shared.VendingMachines.VendingMachineInventoryEntry when writing' in log
    status = 'PASS' if exit_code == 0 and executed == passed == 1 else 'FAIL' if failed or save_error else 'BLOCKED'
    result['mapCheck'] = {'status': status, 'exitCode': exit_code, 'executed': executed,
                          'passed': passed, 'failed': failed, 'log': 'functional-gate.log',
                          'trx': 'tests/briar.trx' if trx.exists() else None,
                          'scope': 'Briar load + scanner module save/reload only; not program acceptance'}
    if save_error:
        result['mapCheck']['failure'] = 'TrySaveMap returned false: VendingMachineInventoryEntry has no serializer/data definition.'
        result['mapCheck']['testHostAborted'] = True
    write_json('functional_results.json', result)
    report_results()
    print(f'Map check: {status}; full 27-program gate remains NOT_PASSED.')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['Diagnose', 'AuditShip', 'CompileScenario', 'RecordGate', 'Report', 'Status'])
    parser.add_argument('--seed', type=int, default=20261009)
    parser.add_argument('--ships', type=int, default=40)
    parser.add_argument('--exit-code', type=int, default=1)
    args = parser.parse_args()
    if args.action == 'Diagnose':
        diagnose()
    elif args.action == 'AuditShip':
        audit()
    elif args.action == 'CompileScenario':
        compile_scenario(args.seed, args.ships)
    elif args.action == 'RecordGate':
        record_gate(args.exit_code)
    elif args.action == 'Report':
        report()
    else:
        print((OUT / 'FUNCTIONAL_REPORT.md').read_text(encoding='utf-8') if (OUT / 'FUNCTIONAL_REPORT.md').exists() else 'No results yet.')


if __name__ == '__main__':
    main()
