"""Package recorded runs without game databases or generated performance data."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def package(series, destination):
    series = series.resolve()
    repo = Path(__file__).resolve().parents[2]
    state = json.loads((series / 'status.json').read_text(encoding='utf-8-sig'))
    files = {}

    def add(path, name):
        if path.is_file():
            files[name] = path

    for path in series.iterdir():
        add(path, 'series/' + path.name)
    for path in (series.parent / 'preflight').rglob('*'):
        if 'data' not in path.relative_to(series.parent / 'preflight').parts:
            add(path, 'preflight/' + path.relative_to(series.parent / 'preflight').as_posix())
    runs = list(state.get('completedRuns', []))
    for path in sorted((series.parent / 'runs').glob('*')):
        if path.is_dir() and not any(Path(run['path']).resolve() == path.resolve() for run in runs):
            runs.append({'path': str(path)})
    for run in runs:
        folder = Path(run['path'])
        for path in folder.iterdir():
            add(path, 'runs/' + folder.name + '/' + path.name)
    for directory in ('Tools/KiasBenchmark', 'Content.Server/_Forge/KIAS',
                      'Content.Shared/_Forge/KIAS', 'Content.IntegrationTests/Tests/_Forge/KIAS'):
        for path in (repo / directory).rglob('*'):
            if path.suffix in ('.cs', '.py', '.ps1', '.md'):
                add(path, 'sources/' + path.relative_to(repo).as_posix())
    for path in (repo/'Docs/KIAS/History').glob('0*.md'):
        add(path, 'specification/' + path.name)
    for name in ('Tools/KiasBenchmark/RUNBOOK.md', 'Resources/Prototypes/_Forge/KIAS/controller_presets.yml',
                 'Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml',
                 'Resources/Prototypes/Entities/Structures/Specific/Anomaly/anomalies.yml'):
        add(repo / name, 'sources/' + name)
    for path in (series.parent / 'tests').glob('*'):
        add(path, 'functional-tests/' + path.name)
    add(series.parent / 'functional-gate.log', 'functional-tests/gate.log')
    for path in (series.parent / 'functional').rglob('*'):
        add(path, 'functional-tests/physical/' + path.relative_to(series.parent / 'functional').as_posix())
    for name in ('flesh-kias.trx', 'flesh-anomaly.trx'):
        add(repo / '.kias-benchmark/tests' / name, 'prior-functional-tests/' + name)
    for path in (repo / '.kias-benchmark/flesh-physical').glob('*'):
        add(path, 'prior-functional-tests/physical/' + path.name)
    guide = f'''# KIAS: данные для анализа

Статус данной серии: {state['status']}.
Ожидаемый порядок A–B–B–A, {state['ships']} кораблей, 60 TPS, каждый прогон:
{state['warmupTicks']} тиков прогрева + {state['measuredTicksPerRun']} тиков измерения. A — база, B — KIAS.
Если статус FAILED_INCOMPLETE, архив содержит только доступные данные;
это не законченная серия и не основание для объявления PASS.

Начните с series/status.json, results.json и report.html (последние два
есть только после успешной проверки серии). Исходные данные в runs:
ticks.csv, events.jsonl, progress.jsonl, manifests, server logs и Prometheus.
В HTML ссылки на локальные исходники могут вести к машине автора;
используйте соответствующие файлы в runs. SHA256SUMS.txt проверяет архив.

Сравните A1/B1 и A2/B2 отдельно, затем фазы IDLE/MIXED/BURST/RECOVERY.
Смотрите p99/p99.9, тики сверх 16.667 мс, аллокации, GC, CPU и память.
Не удаляйте выбросы. Последовательные тики не являются независимыми
повторами опыта. TickStart/Stop не включает frame input/update вне Tick.
Гистограммы Update включают синхронные обработчики, это не профиль методов.
Минутные очереди не доказывают максимальный backlog. Интервалы Prometheus
отличаются от точного интервала 72000 тиков.

Аномалия — AnomalyFlesh с явным прототипом и проверкой опасных компонентов;
в нагрузке удаляется до первого импульса, сохраняет штатную смену тайлов.
При просадке Running проверьте activeMachines, Fault, dirty topology,
восстановление за 120 тиков и recoveredSameMachines.

Покрытие ограничено семью парами драйверов. Нет FTL, стыковки, стрельбы,
столкновений и разрыва DATA. Экипаж нагрузки без зарегистрированных KIAS
транспондеров не доказывает исполнение CrewCritical. Полное равенство миров
и причинные задержки до действий не доказаны. Полный benchmark PASS
по этой серии объявлять нельзя.

prior-functional-tests — прошлые проверки, они не запускаются повторно
батником и не подтверждают автоматически последующие изменения кода.
sources — код на момент упаковки; манифесты фиксируют измеренные сборки.

Задача агенту: проверить целостность и сравнимость, выделить добавочную
нагрузку систем, сопоставить с кодом, отделить доказательства от гипотез,
предложить приоритеты оптимизации и дополнительные замеры.
'''
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix('.zip.tmp')
    try:
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            archive.writestr('README.md', guide)
            for name, path in sorted(files.items()):
                archive.write(path, name)
            archive.writestr('SHA256SUMS.txt', ''.join(
                hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + name + '\n'
                for name, path in sorted(files.items())))
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None:
                raise ValueError('Archive integrity check failed.')
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)
    return destination


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('series', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(package(args.series, args.destination))
