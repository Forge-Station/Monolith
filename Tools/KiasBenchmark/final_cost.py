"""Publish immutable study evidence with calibration, optimization and current-code provenance."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import statistics
from source_fingerprint import ROOT


def load(path):return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def publish(study,comparison,output):
    study,comparison,output=map(Path,(study,comparison,output))
    output.mkdir(parents=True,exist_ok=False)
    cost=load(study/"cost-attribution.json");optimization=load(comparison/"optimization-comparison.json")
    assert len(cost['runs'])==51 and len(optimization['runs'])==21,"Incomplete replicated study"
    assert all(c['aReplicates']>=3 and c['bReplicates']>=3 and not c['largeUnknown'] for c in cost['comparisons'])
    assert len(cost['comparisons'])==6
    after=load(comparison/'AFTER_MANIFEST.json')
    frozen=load(Path(after['builds'][0]['source']).parents[1]/'source-fingerprint.json')
    current=[]
    for name,expected in frozen['files'].items():
        if name.startswith(('Content.Server/_Forge/KIAS/','Content.Shared/_Forge/KIAS/','Content.Client/_Forge/KIAS/','Resources/Prototypes/_Forge/KIAS/')):
            actual=hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
            assert actual==expected,f'Production code changed since measurement: {name}'
            current.append(dict(path=name,sha256=actual))
    assert current
    calibrations=[]
    for probe in ('NORMAL','INSTRUMENT_OFF','PHASE_OFF','RUNTIME_OFF','ROOM_SCAN_OFF'):
        for variant in ('A','B'):
            runs=[r['measurement'] for r in cost['runs'] if r['ships']==40 and r['regime']=='IDLE' and r['probe']==probe and r['variant']==variant]
            if runs:calibrations.append(dict(probe=probe,variant=variant,replicates=len(runs),meanMs=statistics.mean(r['whole']['meanMs'] for r in runs),p50Ms=statistics.mean(r['whole']['p50Ms'] for r in runs)))
    details=[]
    for item in cost['runs']+optimization['runs']:
        path=Path(item['path'])
        with (path/'ticks.csv').open(encoding='utf-8-sig',newline='') as stream:rows=list(csv.DictReader(stream))
        cpu=[(int(row['relative_tick']),float(row['cpu_seconds'])) for row in rows if int(row['relative_tick'])%60==0]
        process_cpu=(cpu[-1][1]-cpu[0][1])*1000/(cpu[-1][0]-cpu[0][0]) if len(cpu)>1 else None
        hist=load(path/'phase-histograms.json');config=load(path/'profile-config.json');phases=[]
        before={p['name']:p for p in hist.get('before') or []}
        for phase in hist.get('after') or []:
            previous=before.get(phase['name'],dict(calls=0,buckets=[0]*32))
            calls=phase['calls']-previous['calls']
            buckets=[a-b for a,b in zip(phase['buckets'],previous['buckets'])]
            assert calls==sum(buckets) and all(n>=0 for n in buckets)
            if not calls:continue
            def bounds(fraction):
                cumulative=0
                for index,count in enumerate(buckets):
                    cumulative+=count
                    if cumulative>=calls*fraction:
                        return [0 if index==0 else 2**index*1000/config['stopwatchFrequency'],(2**(index+1)-1)*1000/config['stopwatchFrequency']]
            phases.append(dict(name=phase['name'],calls=calls,inclusiveP95MsBucket=bounds(.95),inclusiveP99MsBucket=bounds(.99)))
        details.append(dict(path=str(path.resolve()),processCpuMsPerTick=process_cpu,processCpuSampleSpanTicks=cpu[-1][0]-cpu[0][0],
            inclusiveInvocationQuantileBounds=phases,
            gcPauseMs=sum(float(row['gc_pause_ms']) for row in rows),threadAllocatedBytes=sum(int(row['thread_allocated_bytes']) for row in rows),
            top100Spikes=sorted(rows,key=lambda row:float(row['whole_tick_ms']),reverse=True)[:100]))
    result=dict(status='ATTRIBUTION_COMPLETE',fullABBA=False,currentProductionSources=current,
        baselineStudy=cost,optimization=optimization,calibration=calibrations,runDetails=details,
        caveats=["Exclusive elapsed wall times include GC and preemption; process CPU includes worker threads, frame work and output after TickStop.",
                 "Profiler checkpoints are written after the whole-tick timestamp. JSON preparation is counted in native driver/observer; normal long replay retains bounded checkpoints and logs their cost.",
                 "GC overlaps wall scopes and is not added as another exclusive category. P50 differences are not sums of subsystem medians.",
                 "Before/after whole-mean confidence intervals cross zero: a whole-tick speedup is not established. Crew-specific causal reductions and small candidates are reported separately.",
                 "Production KIAS sources match optimized measurements; later harness changes add continuous network history and repeated docking, which will be measured separately in FullGame.",
                 "This attribution does not certify native FullGame scenarios or readiness. Those gates remain mandatory."],
        evidence=dict(study=str(study.resolve()),optimization=str(comparison.resolve())))
    (output/'cost-attribution.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    text=['# Стоимость KIAS','',result['status'],'','51 реплицированный профиль и 21 сравнение оптимизаций завершены. Это отчёт об измерениях, допуск к ABBA проверяется отдельно.','']
    for c in cost['comparisons']:
        text.extend([f"## {c['ships']} кораблей, {c['regime']}",'',f"B−A: mean {c['deltaMeanMs']:.6f} мс/тик; разность p50 {c['deltaP50Ms']:.6f} мс; 95% block CI mean {c['meanDeltaBlock95Ci']}.",'',
            '| Источник | A exclusive mean | B exclusive mean | Разность мс/тик | B calls/tick | B bytes/tick |','|---|---:|---:|---:|---:|---:|'])
        for r in c['rankedExclusiveMean']:text.append(f"| {r['group']} | {r['aMeanMs']:.6f} | {r['bMeanMs']:.6f} | {r['deltaMeanMs']:.6f} | {r['bCallsPerTick']:.3f} | {r['bExclusiveBytesPerTick']:.1f} |")
        text.append('')
    text+=['## Причинные пробы и калибровка','', '| Проба IDLE40 | Вариант | mean мс | p50 мс |','|---|---|---:|---:|']
    for c in calibrations:text.append(f"| {c['probe']} | {c['variant']} | {c['meanMs']:.6f} | {c['p50Ms']:.6f} |")
    text+=['','## Адресные оптимизации','',
           '1. Фильтрация типа Crew перед IsOnline устраняет запросы IntegrationControl для посторонних устройств. Причинная проба восстанавливает старый порядок.',
           '2. Объединение модулей сканера в первом проходе сохраняет тот же результат Contains и убирает второй проход. Выигрыш мал, ускорение полного тика не доказано.',
           '3. Пустой Runtime проверяет очереди и ближайший таймер перед обходами. Экономия мала; отдельная проба возвращает старый путь.', '',
           'Physics оставлен с рабочими коллизиями. Полные инвентари физических тел, fixtures и proxies сохранены в physics-before/final.json; дополнительные тела и общие системы входят в полную стоимость функции.', '',
           '| Режим | Старый путь | Сэкономленный mean мс | Разность p50 мс | 95% CI mean |','|---|---|---:|---:|---|']
    for c in optimization['comparisons']:text.append(f"| {c['regime']} | {c['restored']} | {c['savedMeanMs']:.6f} | {c['savedP50Ms']:.6f} | {c['savedMeanBlock95Ci']} |")
    text+=['','## Ограничения','']+['- '+c for c in result['caveats']]+['','## Сырые данные','',f"- [Профили и архив]({study.resolve().as_posix()})",f"- [Сравнение оптимизаций и архив]({comparison.resolve().as_posix()})",'']
    (output/'COST_OF_KIAS.md').write_text('\n'.join(text),encoding='utf-8')
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('study');parser.add_argument('comparison');parser.add_argument('output')
    args=parser.parse_args();publish(args.study,args.comparison,args.output)
