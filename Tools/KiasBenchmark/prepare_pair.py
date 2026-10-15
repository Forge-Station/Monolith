"""Prepare isolated common-base sources and prove non-KIAS map inventory parity."""
from pathlib import Path
import argparse
import hashlib,json,shutil,subprocess,yaml
from lab import ROOT,OUT,MAP,MapLoader,canonical,digest

def git(*args,cwd=ROOT):
    return subprocess.run(['git',*args],cwd=cwd,check=True,capture_output=True).stdout

class Tagged(dict):
    pass
class Loader(yaml.SafeLoader):
    pass
class Dumper(yaml.SafeDumper):
    pass

def tagged(loader,suffix,node):
    value=Tagged(loader.construct_mapping(node,deep=True))
    value.tag='!type:'+suffix
    return value
Loader.add_multi_constructor('!type:',tagged)
Dumper.add_representer(Tagged,lambda dumper,value:dumper.represent_mapping(value.tag,value.items()))

def vanilla(source):
    value=yaml.load(source,Loader=Loader)
    removed={e['uid'] for g in value['entities'] if 'Kias' in g.get('proto','') for e in g['entities']}
    # Р‘РµСЂС‘Рј СѓРґР°Р»С‘РЅРЅС‹Рµ СѓСЃС‚СЂРѕР№СЃС‚РІР°, РёСЃРєР»СЋС‡Р°РµРј С‚Р°РєР¶Рµ РёС… РІР»РѕР¶РµРЅРЅС‹Рµ РїСЂРµРґРјРµС‚С‹.
    while True:
        children={e['uid'] for g in value['entities'] for e in g['entities']
                  if any(c['type']=='Transform' and c.get('parent') in removed for c in e.get('components',[]))}
        added=children-removed
        if not added: break
        removed.update(added)
    groups=[]
    for group in value['entities']:
        if 'Kias' in group.get('proto',''): continue
        group['entities']=[e for e in group['entities'] if e['uid'] not in removed]
        if not group['entities']: continue
        for entity in group['entities']:
            entity['components']=[c for c in entity.get('components',[]) if not c['type'].startswith('Kias')]
            for component in entity['components']:
                if component['type']=='ContainerContainer':
                    for name,container in component.get('containers',{}).items():
                        if container.get('ent') in removed: container['ent']=None
                        if 'ents' in container: container['ents']=[uid for uid in container['ents'] if uid not in removed]
        groups.append(group)
    value['entities']=groups
    value['meta']['entityCount']=sum(len(g['entities']) for g in groups)
    return value,removed

def main():
    global OUT
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', type=Path)
    parser.add_argument('--base')
    options = parser.parse_args()
    legacy_fingerprint = OUT / 'fingerprint.json'
    if options.directory:
        OUT = options.directory.resolve()
    OUT.mkdir(exist_ok=True)
    head=git('rev-parse','HEAD').decode().strip()
    base=options.base or json.loads(legacy_fingerprint.read_text(encoding='utf-8'))['commonBase']
    git('merge-base','--is-ancestor',base,'HEAD')
    engine=git('rev-parse','HEAD',cwd=ROOT/'RobustToolbox').decode().strip()
    manifest={'status':'SOURCES_PREPARED_NOT_BUILT','base':base,'branchHead':head,'engine':engine,'builds':[],'runtimeParity':'NOT_TESTED'}
    bpatch=git('diff','--binary',base,'HEAD')
    (OUT/'matched-overlay.patch').write_bytes(bpatch)
    dirty=git('diff','--binary','HEAD')
    (OUT/'matched-current-fixes.patch').write_bytes(dirty)
    shared_overrides = ('Content.Client/_Mono/Audio/AudioEffectSystem.cs',
                        'Content.Server/_Mono/NPC/HTN/Operators/ShipMoveToOperator.cs',
                        'Content.Server/Chat/Managers/ChatSanitizationManager.cs')
    manifest['sharedSourceOverrides'] = {name: digest((ROOT / name).read_bytes()) for name in shared_overrides}
    paths=[]
    for label in ('A','B'):
        target=OUT/'builds'/label
        if target.exists(): raise RuntimeError(f'Refusing to overwrite existing sources: {target}')
        target.parent.mkdir(exist_ok=True)
        git('worktree','add','--detach',str(target),base)
        paths.append(target)
        if label=='B':
            subprocess.run(['git','apply','--binary','-'],input=bpatch,cwd=target,check=True,capture_output=True)
            subprocess.run(['git','apply','--binary','-'],input=dirty,cwd=target,check=True,capture_output=True)
            for name in ('Content.Server/_Forge/KIAS/KiasAmeAdapterSystem.cs',):
                shutil.copy2(ROOT/name,target/name)
            untracked = git('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
            for name in untracked:
                if not name or not name.startswith(('Content.Server/_Forge/KIAS/', 'Content.Shared/_Forge/KIAS/',
                    'Content.Client/_Forge/KIAS/', 'Content.IntegrationTests/Tests/_Forge/KIAS/',
                    'Resources/Locale/ru-RU/_Forge/KIAS/', 'Resources/Locale/en-US/_Forge/KIAS/')):
                    continue
                destination = target / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / name, destination)
        if label == 'B':
            runtime = target/'Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.cs'
            code = runtime.read_text(encoding='utf-8-sig')
            code = code.replace('public override void Update(float frameTime)\n    {',
                'public override void Update(float frameTime)\n    {\n        if (Content.Server.Benchmark.NativeLab.PauseRuntime) return;')
            code = code.replace('private void OnEmission(EntityUid device, string profile, string port, KiasGraphValue value)\n    {',
                'private void OnEmission(EntityUid device, string profile, string port, KiasGraphValue value)\n    {\n        if (Content.Server.Benchmark.NativeLab.PauseRuntime) return;')
            runtime.write_text(code, encoding='utf-8')
        if label == 'B':
            crew_path=target/'Content.Server/_Forge/KIAS/KiasCrewSystem.cs'
            crew_code=crew_path.read_text(encoding='utf-8-sig')
            anchor='public override void Update(float frameTime)\n    {'
            assert crew_code.count(anchor)==1
            crew_path.write_text(crew_code.replace(anchor,anchor+'\n        if (Content.Server.Benchmark.NativeLab.PauseRoomScans) return;'),encoding='utf-8')
            from instrument_optimization_probes import instrument as instrument_optimizations
            instrument_optimizations(target)
        impact_relative = 'Content.Server/Shuttles/Systems/ShuttleSystem.Impact.cs'
        impact_current = (ROOT / impact_relative).read_text(encoding='utf-8-sig')
        signal_start = impact_current.index('        var relativeSpeed =')
        signal_end = impact_current.index('        // TODO: Would also be nice', signal_start)
        impact_neutral = impact_current[:signal_start] + impact_current[signal_end:]
        assert 'KIAS' not in impact_neutral
        impact_source = impact_current if label == 'B' else impact_neutral
        (target / impact_relative).write_text(impact_source, encoding='utf-8')
        manifest.setdefault('commonImpactFix', {})[label] = {
            'sourceSha256': digest(impact_source.encode('utf-8')),
            'neutralBodySha256': digest(impact_neutral.encode('utf-8')),
            'difference': 'B-only KIAS collision event emission; accepted-impact deduplication identical'}
        lab_source=ROOT/'Tools/KiasBenchmark/Server/NativeLab.cs'
        lab_target=target/'Tools/KiasBenchmark/Server/NativeLab.cs'
        lab_target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(lab_source,lab_target)
        shutil.copy2(ROOT/'Tools/KiasBenchmark/Server/NativeGameDrivers.cs', target/'Tools/KiasBenchmark/Server/NativeGameDrivers.cs')
        shutil.copy2(ROOT/'Tools/KiasBenchmark/Server/NativeProfiler.cs', target/'Tools/KiasBenchmark/Server/NativeProfiler.cs')
        telemetry_include = ''
        if label == 'B':
            shutil.copy2(ROOT/'Tools/KiasBenchmark/Server/KiasTelemetry.cs', target/'Tools/KiasBenchmark/Server/KiasTelemetry.cs')
            telemetry_include = '    <Compile Include="../Tools/KiasBenchmark/Server/KiasTelemetry.cs" Link="KiasTelemetry.cs" />\n'
        project=target/'Content.Server/Content.Server.csproj'
        source=project.read_text(encoding='utf-8-sig').replace('</Project>', '  <ItemGroup>\n    <Compile Include="../Tools/KiasBenchmark/Server/NativeLab.cs" Link="NativeLab.cs" />\n    <Compile Include="../Tools/KiasBenchmark/Server/NativeGameDrivers.cs" Link="NativeGameDrivers.cs" />\n    <Compile Include="../Tools/KiasBenchmark/Server/NativeProfiler.cs" Link="NativeProfiler.cs" />\n'+telemetry_include+'  </ItemGroup>\n</Project>')
        project.write_text(source,encoding='utf-8')
        entry=target/'Content.Server/Entry/EntryPoint.cs'
        source=entry.read_text(encoding='utf-8-sig')
        hook='            base.Update(level, frameEventArgs);'
        assert source.count(hook)==1
        source=source.replace(hook,'            if (level == ModUpdateLevel.PreEngine) Content.Server.Benchmark.NativeLab.PreTick();\n'+hook+'\n            if (level == ModUpdateLevel.PostEngine) Content.Server.Benchmark.NativeLab.PostTick();')
        entry.write_text(source,encoding='utf-8')
        for name in shared_overrides:
            shutil.copy2(ROOT / name, target / name)
            assert digest((target / name).read_bytes()) == manifest['sharedSourceOverrides'][name]
        engine_path=target/'RobustToolbox'
        engine_path.rmdir()
        git('worktree','add','--detach',str(engine_path),engine,cwd=ROOT/'RobustToolbox')
        physics = engine_path/'Robust.Server/GameObjects/EntitySystems/PhysicsSystem.cs'
        physics_code = physics.read_text(encoding='utf-8-sig')
        physics_code = physics_code.replace('        public override void Update(float frameTime)',
            '        public static double DiagnosticMilliseconds { get; private set; }\n        public static long DiagnosticAllocatedBytes { get; private set; }\n\n        public override void Update(float frameTime)')
        physics_code = physics_code.replace('            SimulateWorld(frameTime, false);',
            '            var stamp = System.Diagnostics.Stopwatch.GetTimestamp();\n            var bytes = System.GC.GetAllocatedBytesForCurrentThread();\n            SimulateWorld(frameTime, false);\n            DiagnosticMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;\n            DiagnosticAllocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - bytes;')
        physics.write_text(physics_code, encoding='utf-8')
        from instrument_physics_queries import instrument
        query_instrumentation = instrument(engine_path)
        from instrument_system_profile import instrument as instrument_systems
        system_instrumentation = instrument_systems(engine_path)
        (target/'system-instrumentation.json').write_text(json.dumps(system_instrumentation, indent=2), encoding='utf-8')
        (target/'physics-query-instrumentation.json').write_text(json.dumps(query_instrumentation, indent=2), encoding='utf-8')
        modules=git('config', '-f', '.gitmodules', '--get-regexp', r'^submodule\..*\.path$', cwd=ROOT/'RobustToolbox').decode().splitlines()
        for line in modules:
            relative=line.strip().split(maxsplit=1)[1]
            empty=engine_path/relative
            if empty.exists(): empty.rmdir()
            command = "New-Item -ItemType Junction -Path '" + str(empty).replace("'", "''") + "' -Target '" + str(ROOT/'RobustToolbox'/relative).replace("'", "''") + "' | Out-Null"
            subprocess.run(['powershell','-NoProfile','-Command',command],check=True,capture_output=True)
        manifest['builds'].append({'label':label,'source':str(target),'base':base,'engine':engine,'compiled':False})
    amap,removed=vanilla(MAP.read_text(encoding='utf-8'))
    map_relative='Resources/Maps/_Forge/Shuttles/Archive/Mercenary/labBriar.yml'
    for target in paths: (target/map_relative).parent.mkdir(parents=True,exist_ok=True)
    (paths[0]/map_relative).write_text(yaml.dump(amap,Dumper=Dumper,allow_unicode=True,sort_keys=False),encoding='utf-8')
    shutil.copy2(MAP,paths[1]/map_relative)
    apart=yaml.load((paths[0]/map_relative).read_text(encoding='utf-8'),Loader=MapLoader)
    bpart,_=vanilla((paths[1]/map_relative).read_text(encoding='utf-8'))
    bpart=yaml.load(yaml.dump(bpart,Dumper=Dumper,allow_unicode=True,sort_keys=False),Loader=MapLoader)
    assert canonical(apart)==canonical(bpart),'Non-KIAS content parity failed'
    parity={'status':'STATIC_VANILLA_PARITY_PASS_RUNTIME_NOT_TESTED','nativeEntityCount':amap['meta']['entityCount'],'removedKiasEntities':len(removed),'nonKiasMapSha256':digest(canonical(apart)),'aMapSha256':digest((paths[0]/map_relative).read_bytes()),'bMapSha256':digest((paths[1]/map_relative).read_bytes())}
    (OUT/'static-parity.json').write_text(json.dumps(parity,indent=2),encoding='utf-8')
    manifest.update(overlaySha256=digest(bpatch),currentFixesSha256=digest(dirty),staticParity=parity)
    (OUT/'BASELINE_MANIFEST.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(parity,indent=2))

if __name__=='__main__': main()
