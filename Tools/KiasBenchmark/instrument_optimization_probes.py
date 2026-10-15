"""Restore individual old algorithms only in an isolated profiling build."""
from pathlib import Path


def instrument(source):
    source=Path(source).resolve()
    if source==Path(__file__).resolve().parents[2]:
        raise ValueError("Refusing to instrument production sources")
    crew=source/"Content.Server/_Forge/KIAS/KiasCrewSystem.cs"
    code=crew.read_text(encoding="utf-8-sig")
    anchor="    private KiasScannerModules Modules(EntityUid scanner) => TryComp<KiasRoomScannerComponent>(scanner, out var component) && _kias.IsOnline(scanner)\n        ? component.Modules : KiasScannerModules.None;"
    assert code.count(anchor)==1
    code=code.replace(anchor,"""    private KiasScannerModules Modules(EntityUid scanner)
    {
        if (Content.Server.Benchmark.NativeLab.DisableCrewTypeFilter)
            return _kias.IsOnline(scanner) && TryComp<KiasRoomScannerComponent>(scanner, out var previous)
                ? previous.Modules : KiasScannerModules.None;
        return TryComp<KiasRoomScannerComponent>(scanner, out var component) && _kias.IsOnline(scanner)
            ? component.Modules : KiasScannerModules.None;
    }""")
    anchor="            if (TryComp<KiasCrewServerComponent>(uid, out var server) && _kias.IsOnline(uid))"
    assert code.count(anchor)==1
    code=code.replace(anchor,"""            if (Content.Server.Benchmark.NativeLab.DisableCrewTypeFilter && !_kias.IsOnline(uid)) continue;
            if (TryComp<KiasCrewServerComponent>(uid, out var server)
                && (Content.Server.Benchmark.NativeLab.DisableCrewTypeFilter || _kias.IsOnline(uid)))""")
    anchor="                    combined |= modules;"
    assert code.count(anchor)==1
    code=code.replace(anchor,"                    if (!Content.Server.Benchmark.NativeLab.DisableCoverageReuse) combined |= modules;")
    anchor="                if (counted)\n                    entities++;"
    assert code.count(anchor)==1
    code=code.replace(anchor,"""                if (Content.Server.Benchmark.NativeLab.DisableCoverageReuse)
                    foreach (var scanner in scanners)
                    {
                        var modules = Modules(scanner);
                        if (_rooms.Contains(scanner, person, modules)) combined |= modules;
                    }
"""+anchor)
    crew.write_text(code,encoding="utf-8")
    runtime=source/"Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.cs"
    code=runtime.read_text(encoding="utf-8-sig")
    anchor="        if (_boots.Count == 0 && _events.Count == 0 && _dispatch == null && _work.Count == 0 && _commands.Count == 0"
    assert code.count(anchor)==1
    runtime.write_text(code.replace(anchor,anchor.replace("if (","if (!Content.Server.Benchmark.NativeLab.DisableRuntimeIdleFastPath && ",1)),encoding="utf-8")
