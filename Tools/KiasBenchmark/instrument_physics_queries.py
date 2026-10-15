"""Instrument isolated engine worktrees only, without changing production physics."""
import argparse
import hashlib
import json
from pathlib import Path

SIGNATURES = [
    "public void Query(Box2 aabb, uint maskBits, TreeQueryCallback callback)",
    "public void Query<TState>(ref TState state, QueryCallback<TState> callback, in Box2 aabb)",
    "public void Query<TState>(ref TState state, QueryCallback<TState> callback, Vector2 point)",
    "public void FastQuery(ref Box2 aabb, FastQueryCallback callback)",
    "public void RayCast<TState>(ref TState state, RayQueryCallback<TState> callback, in Ray input)",
]


def instrument(engine):
    engine = Path(engine).resolve()
    production = Path(__file__).resolve().parents[2] / 'RobustToolbox'
    if engine == production.resolve() or engine.is_relative_to(production.resolve()):
        raise ValueError('Refusing to instrument production physics sources')
    path = engine / "Robust.Shared/Physics/B2DynamicTree.cs"
    code = path.read_text(encoding="utf-8-sig")
    if "DiagnosticQueryCount" not in code:
        anchor = "        #region Queries"
        assert code.count(anchor) == 1, "Unexpected engine query layout"
        code = code.replace(anchor,
            "        private static long _diagnosticQueryCount;\n"
            "        public static long DiagnosticQueryCount => System.Threading.Interlocked.Read(ref _diagnosticQueryCount);\n\n" + anchor)
        for signature in SIGNATURES:
            anchor = "        " + signature + "\n        {"
            assert code.count(anchor) == 1, signature
            code = code.replace(anchor, anchor + "\n            System.Threading.Interlocked.Increment(ref _diagnosticQueryCount);")
    if "DiagnosticQueriesEnabled" not in code:
        anchor = "        private static long _diagnosticQueryCount;"
        assert code.count(anchor) == 1, "Unexpected existing query instrumentation"
        code = code.replace(anchor, anchor + '\n        public static bool DiagnosticQueriesEnabled { get; } = '
                            'System.Environment.GetEnvironmentVariable("NATIVE_LAB_PHYSICS_QUERY_COUNTERS") != "0";')
        increment = "System.Threading.Interlocked.Increment(ref _diagnosticQueryCount);"
        assert code.count(increment) == len(SIGNATURES), "Unexpected query counter count"
        code = code.replace(increment, "if (DiagnosticQueriesEnabled) " + increment)
    path.write_text(code, encoding="utf-8")
    return dict(path=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                scope="One count per B2DynamicTree<T> traversal, wrappers excluded. FixtureProxy specialization includes collision broadphase and physics query traversals.",
                instrumentedEntryPoints=SIGNATURES, instrumentationVersion=2,
                calibration="NATIVE_LAB_PHYSICS_QUERY_COUNTERS=0 disables increments; branch and other instrumentation remain")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("engine", type=Path)
    args = parser.parse_args()
    print(json.dumps(instrument(args.engine), indent=2))
