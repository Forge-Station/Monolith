"""Add optional timing hooks only to isolated matched engine worktrees."""
from pathlib import Path
import hashlib


def instrument(engine):
    engine=Path(engine).resolve()
    production=Path(__file__).resolve().parents[2]/"RobustToolbox"
    if engine==production.resolve():
        raise ValueError("Refusing to instrument production engine")
    path=engine/"Robust.Shared/GameObjects/EntitySystemManager.cs"
    code=path.read_text(encoding="utf-8-sig")
    if "DiagnosticBeforeSystem" not in code:
        anchor="    public sealed partial class EntitySystemManager : IEntitySystemManager, IPostInjectInit\n    {"
        assert code.count(anchor)==1,"Unexpected system manager definition"
        code=code.replace(anchor,anchor+"\n        public static Action<string>? DiagnosticBeforeSystem;\n        public static Action<string, long, long>? DiagnosticAfterSystem;\n        public static Action<Exception>? DiagnosticSystemFailure;\n")
        anchor="                    using (_profManager.Value(updReg.ProfileName))"
        assert code.count(anchor)==2,"Unexpected system timing anchors"
        position=code.index(anchor,code.index("public void TickUpdate("))
        code=code[:position]+code[position:].replace(anchor,"""                    var diagnostic = DiagnosticBeforeSystem != null;
                    long diagnosticStamp = 0, diagnosticBytes = 0;
                    if (diagnostic)
                    {
                        DiagnosticBeforeSystem!(updReg.ProfileName);
                        diagnosticBytes = GC.GetAllocatedBytesForCurrentThread();
                        diagnosticStamp = Stopwatch.GetTimestamp();
                    }
"""+anchor,1)
        anchor="                        updReg.System.Update(frameTime);\n                    }"
        assert code.count(anchor)==1
        replacement="""                    try
                    {
                        using (_profManager.Value(updReg.ProfileName))
                        {
                            updReg.System.Update(frameTime);
                        }
                    }
                    catch (Exception error)
                    {
                        DiagnosticSystemFailure?.Invoke(error);
                        throw;
                    }"""
        original="                    using (_profManager.Value(updReg.ProfileName))\n                    {\n                        updReg.System.Update(frameTime);\n                    }"
        assert code.count(original)==1
        code=code.replace(original,replacement+"""
                    if (diagnostic)
                    {
                        var elapsed = Stopwatch.GetTimestamp() - diagnosticStamp;
                        DiagnosticAfterSystem?.Invoke(updReg.ProfileName, elapsed, GC.GetAllocatedBytesForCurrentThread() - diagnosticBytes);
                    }
""")
        path.write_text(code,encoding="utf-8")
    code=path.read_text(encoding="utf-8-sig")
    if "DiagnosticLoopScope" not in code:
        anchor="        public static Action<Exception>? DiagnosticSystemFailure;"
        assert code.count(anchor)==1
        code=code.replace(anchor,anchor+"""
        public readonly struct DiagnosticLoopScope : IDisposable
        {
            private readonly string? _name;
            private readonly long _stamp, _bytes;
            public DiagnosticLoopScope(string name)
            {
                _name = DiagnosticBeforeSystem != null ? name : null;
                _stamp = _bytes = 0;
                if (_name == null) return;
                DiagnosticBeforeSystem!(_name);
                _bytes = GC.GetAllocatedBytesForCurrentThread();
                _stamp = Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (_name == null) return;
                var elapsed = Stopwatch.GetTimestamp() - _stamp;
                DiagnosticAfterSystem?.Invoke(_name, elapsed, GC.GetAllocatedBytesForCurrentThread() - _bytes);
            }
        }
""")
        path.write_text(code,encoding="utf-8")
    macro_paths=[]
    for relative,anchors in {
        "Robust.Server/BaseServer.cs": [
            ('            using (TickUsage.WithLabels("NetworkedCVar").NewTimer())',"NetworkedCVar"),
            ('            using (TickUsage.WithLabels("Timers").NewTimer())',"Timers"),
            ('            using (TickUsage.WithLabels("AsyncTasks").NewTimer())',"AsyncTasks"),
            ('            using (TickUsage.WithLabels("GameState").NewTimer())',"GameState")],
        "Robust.Shared/GameObjects/EntityManager.cs": [
            ('            using (_entityEventBusHistogram?.NewTimer())',"EventQueue"),
            ('            using (_queuedDeletionHistogram?.NewTimer())',"QueuedDeletion"),
            ('            using (_componentCullHistogram?.NewTimer())',"ComponentCull")],
    }.items():
        macro_path=engine/relative
        macro_code=macro_path.read_text(encoding="utf-8-sig")
        for anchor,name in anchors:
            marker=f'            using (new Robust.Shared.GameObjects.EntitySystemManager.DiagnosticLoopScope("Engine.{name}"))'
            if marker in macro_code: continue
            assert macro_code.count(anchor)==1,f"Unexpected engine loop anchor: {relative}/{name}"
            macro_code=macro_code.replace(anchor,marker+"\n"+anchor)
        macro_path.write_text(macro_code,encoding="utf-8")
        macro_paths.append(dict(path=str(macro_path),sha256=hashlib.sha256(macro_path.read_bytes()).hexdigest()))
    return dict(path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),loopScopes=macro_paths,scope="Optional server-thread inclusive system and disjoint engine-loop scopes; consumer subtracts nested KIAS phases; hooks OFF by default")
