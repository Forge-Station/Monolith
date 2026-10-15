"""Counterbalanced before/after and individual restoration probes, without acceptance waivers."""
import json
from pathlib import Path
import statistics
from cost_attribution import read_run, block_interval, GROUPS


def report(root):
    root=Path(root)
    state=json.loads((root/"optimization-status.json").read_text(encoding="utf-8-sig"))
    assert state["status"]=="OPTIMIZATION_SAMPLES_COMPLETE"
    runs=[dict(item,measurement=read_run(item["path"])) for item in state["completedRuns"]]
    comparisons=[]
    for regime in ("IDLE","MIXED"):
        for probe in ("BEFORE","CrewFilterOff","CoveragePassOff","RuntimeIdleOff"):
            left=[r["measurement"] for r in runs if r["regime"]==regime and r["probe"]==probe]
            right=[r["measurement"] for r in runs if r["regime"]==regime and r["probe"]=="AFTER"]
            if not left: continue
            assert len(left)>=3 and len(right)>=3,"At least three repetitions required"
            mean=lambda values,key:statistics.mean(r["whole"][key] for r in values)
            comparisons.append(dict(regime=regime,restored=probe,leftRuns=len(left),afterRuns=len(right),
                savedMeanMs=mean(left,"meanMs")-mean(right,"meanMs"),savedP50Ms=mean(left,"p50Ms")-mean(right,"p50Ms"),
                savedMeanBlock95Ci=block_interval([v for r in right for v in r["rawWhole"]],[v for r in left for v in r["rawWhole"]]),
                exclusiveSavedMeanMs={group:statistics.mean(r["groups"][group]["meanMs"] for r in left)-statistics.mean(r["groups"][group]["meanMs"] for r in right) for group in GROUPS}))
    for run in runs:
        run["measurement"].pop("rawWhole");run["measurement"].pop("rawGroups")
    result=dict(status="MEASURED_NOT_GAMEPLAY_CERTIFICATION",comparisons=comparisons,runs=runs,
        caveat="Positive savings favor optimized B. Individual probes restore one old algorithm with other patches enabled; interactions mean individual effects cannot be summed. Whole p50 differences are separate from additive exclusive means. Before and after use identical 40-ship scenarios, four real clients and the same engine; test-only switches add small branch overhead only to AFTER builds. Confidence intervals crossing zero do not establish a whole-tick gain.")
    (root/"optimization-comparison.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
    lines=["# B before / B after","",result["caveat"],"","| Regime | Restored old algorithm | Saved mean ms | Saved p50 ms | Mean 95% block CI |","|---|---|---:|---:|---|"]
    for c in comparisons:lines.append(f"| {c['regime']} | {c['restored']} | {c['savedMeanMs']:.6f} | {c['savedP50Ms']:.6f} | {c['savedMeanBlock95Ci']} |")
    (root/"OPTIMIZATION_COMPARISON.md").write_text("\n".join(lines)+"\n",encoding="utf-8")
    return result


if __name__=="__main__":
    import sys
    report(sys.argv[1])
