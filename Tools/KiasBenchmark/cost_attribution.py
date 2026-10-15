"""Exclusive per-tick accounting; quantiles are never added across systems."""
import csv
import json
import math
from pathlib import Path
import random
import statistics
from collections import defaultdict

GROUPS=("core_topology","crew_rooms_integration","runtime_vm","runtime_io_actuation","other_kias","physics","common_engine","native_driver_observer","residual")


def quantile(values, fraction):
    values=sorted(values)
    if not values: return None
    rank=(len(values)-1)*fraction
    lower=int(rank)
    upper=min(lower+1,len(values)-1)
    return values[lower]+(values[upper]-values[lower])*(rank-lower)


def group(kind,name):
    name=name.rsplit(".",1)[-1]
    if kind=="native": return "native_driver_observer"
    if kind=="phase":
        if name in ("RuntimeCommands","IoCommand","IoEmit"): return "runtime_io_actuation"
        if name.startswith("Runtime"): return "runtime_vm"
        if name.startswith(("Crew","Integration","Room")): return "crew_rooms_integration"
        return "other_kias"
    if name=="PhysicsSystem": return "physics"
    if name.startswith(("KiasCrew","KiasRoom","KiasIntegration")): return "crew_rooms_integration"
    if name.startswith("KiasControllerRuntime"): return "runtime_vm"
    if name.startswith("KiasControllerIo"): return "runtime_io_actuation"
    if name in ("KiasSystem","KiasDeviceIdentitySystem"): return "core_topology"
    return "other_kias" if name.startswith("Kias") else "common_engine"


def read_run(directory):
    directory=Path(directory)
    manifest=json.loads((directory/"run-manifest.json").read_text(encoding="utf-8-sig"))
    completed=json.loads((directory/"completed.json").read_text(encoding="utf-8-sig"))
    config=json.loads((directory/"profile-config.json").read_text(encoding="utf-8-sig"))
    assert not any((directory/name).exists() for name in ("failed.json","run-failed.json")), "Failed run cannot attribute normal cost"
    assert manifest["performanceEligible"] is True, "Contaminated or functional-only run cannot certify CPU cost"
    with (directory/"ticks.csv").open(encoding="utf-8-sig",newline="") as stream: rows=list(csv.DictReader(stream))
    count=completed["measuredTicks"]
    assert len(rows)==count and [int(row["relative_tick"]) for row in rows]==list(range(count)), "Missing or duplicate tick samples"
    whole=[float(row["whole_tick_ms"]) for row in rows]
    assert all(math.isfinite(value) and value>=0 for value in whole), "Invalid whole-tick values"
    costs={name:[0.0]*count for name in GROUPS}
    group_totals={name:dict(calls=0,bytes=0) for name in GROUPS}
    methods=defaultdict(lambda:dict(calls=0,inclusiveMs=0.0,exclusiveMs=0.0,exclusiveBytes=0))
    with (directory/"system-phases.csv").open(encoding="utf-8-sig",newline="") as stream:
        for row in csv.DictReader(stream):
            tick=int(row["relative_tick"]); value=float(row["exclusive_ms"])
            assert 0<=tick<count and math.isfinite(value) and value>=0, "Invalid exclusive phase tick/cost"
            category=group(row["kind"],row["name"])
            costs[category][tick]+=value
            group_totals[category]["calls"]+=int(row["calls"])
            group_totals[category]["bytes"]+=int(row["exclusive_allocated_bytes"])
            method=methods[(row["kind"],row["name"])]
            method["calls"]+=int(row["calls"]);method["inclusiveMs"]+=float(row["inclusive_ms"])
            method["exclusiveMs"]+=value;method["exclusiveBytes"]+=int(row["exclusive_allocated_bytes"])
    costs["residual"]=[whole[tick]-sum(costs[name][tick] for name in GROUPS[:-1]) for tick in range(count)]
    assert min(costs["residual"])>=-.05, "Overlapping scopes invalidate exclusive attribution"
    lower,upper=quantile(whole,.45),quantile(whole,.55)
    cohort=[i for i,value in enumerate(whole) if lower<=value<=upper]
    metrics={name:dict(meanMs=statistics.mean(values),p50CohortMeanMs=statistics.mean(values[i] for i in cohort),callsPerTick=group_totals[name]["calls"]/count,exclusiveBytesPerTick=group_totals[name]["bytes"]/count) for name,values in costs.items()}
    return dict(path=str(directory.resolve()),ticks=count,whole=dict(meanMs=statistics.mean(whole),p50Ms=statistics.median(whole),p95Ms=quantile(whole,.95),p99Ms=quantile(whole,.99),p999Ms=quantile(whole,.999),maxMs=max(whole),overBudgetTicks=sum(value>1000/60 for value in whole)),
                groups=metrics,methods=[dict(kind=key[0],name=key[1],**value,meanExclusiveMsPerTick=value["exclusiveMs"]/count,callsPerTick=value["calls"]/count,exclusiveBytesPerTick=value["exclusiveBytes"]/count) for key,value in sorted(methods.items(),key=lambda item:item[1]["exclusiveMs"],reverse=True)],
                rawWhole=whole,rawGroups=costs,config=config,manifest=manifest,
                allocationsBytes=sum(int(row["whole_allocated_bytes"]) for row in rows),gcPauseMs=sum(float(row["gc_pause_ms"]) for row in rows))


def block_interval(a,b,seed=20261013,repetitions=1000):
    rng=random.Random(seed)
    def blocks(values): return [statistics.mean(values[i:i+60]) for i in range(0,len(values),60)]
    left,right=blocks(a),blocks(b)
    samples=[statistics.mean(rng.choices(right,k=len(right)))-statistics.mean(rng.choices(left,k=len(left))) for _ in range(repetitions)]
    return [quantile(samples,.025),quantile(samples,.975)]


def report(directory):
    directory=Path(directory)
    state=json.loads((directory/"profile-status.json").read_text(encoding="utf-8-sig"))
    results=[]
    for item in state.get("completedRuns",[]):
        run=read_run(item["path"])
        results.append(dict(item,measurement=run))
    comparisons=[]
    for ships in (1,10,40):
        for regime in ("IDLE","MIXED"):
            a=[item["measurement"] for item in results if item["ships"]==ships and item["regime"]==regime and item["variant"]=="A" and item["probe"]=="NORMAL"]
            b=[item["measurement"] for item in results if item["ships"]==ships and item["regime"]==regime and item["variant"]=="B" and item["probe"]=="NORMAL"]
            if not a or not b: continue
            delta=statistics.mean(run["whole"]["meanMs"] for run in b)-statistics.mean(run["whole"]["meanMs"] for run in a)
            table=[]
            for name in GROUPS:
                av=statistics.mean(run["groups"][name]["meanMs"] for run in a);bv=statistics.mean(run["groups"][name]["meanMs"] for run in b)
                table.append(dict(group=name,aMeanMs=av,bMeanMs=bv,deltaMeanMs=bv-av,
                    bCallsPerTick=statistics.mean(run["groups"][name]["callsPerTick"] for run in b),
                    bExclusiveBytesPerTick=statistics.mean(run["groups"][name]["exclusiveBytesPerTick"] for run in b),
                    explainedFractionOfMeanDelta=(bv-av)/delta if abs(delta)>1e-9 else None))
            unknown=next(row["deltaMeanMs"] for row in table if row["group"]=="residual")
            comparisons.append(dict(ships=ships,regime=regime,aReplicates=len(a),bReplicates=len(b),deltaMeanMs=delta,
                deltaP50Ms=statistics.mean(run["whole"]["p50Ms"] for run in b)-statistics.mean(run["whole"]["p50Ms"] for run in a),
                meanDeltaBlock95Ci=block_interval([v for run in a for v in run["rawWhole"]],[v for run in b for v in run["rawWhole"]]),
                replicateMeanRanges=dict(A=[min(run["whole"]["meanMs"] for run in a),max(run["whole"]["meanMs"] for run in a)],B=[min(run["whole"]["meanMs"] for run in b),max(run["whole"]["meanMs"] for run in b)]),
                largeUnknown=abs(unknown)>max(.1,.2*abs(delta)),rankedExclusiveMean=sorted(table,key=lambda row:row["deltaMeanMs"],reverse=True)))
    for item in results:
        item["measurement"].pop("rawWhole");item["measurement"].pop("rawGroups")
    result=dict(status="ATTRIBUTION_INCOMPLETE",optimizationComparison="NOT_YET_MEASURED",comparisons=comparisons,runs=results,
        scope="Exclusive mean wall cost is additive. Whole-tick p50 delta is separate; p45-p55 cohorts describe typical ticks but are not a sum-of-medians decomposition. GC pause overlaps measured wall scopes and must not be added again.",
        uncertainty="95% non-overlapping block bootstrap uses 60-tick blocks; independent replicate ranges and instrumentation ON/OFF probes must also be inspected. Profiling and ablation are not full gameplay acceptance.")
    (directory/"cost-attribution.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
    text=["# Cost of KIAS","",result["status"],"",result["scope"],"",result["uncertainty"],""]
    for item in comparisons:
        text.extend([f"## {item['ships']} ships / {item['regime']}","",f"Whole mean delta {item['deltaMeanMs']:.6f} ms/tick; p50 delta {item['deltaP50Ms']:.6f} ms; mean block CI {item['meanDeltaBlock95Ci']}","","| Source | A exclusive mean ms/tick | B exclusive mean ms/tick | Delta | B calls/tick | B exclusive bytes/tick |","|---|---:|---:|---:|---:|---:|"])
        for row in item["rankedExclusiveMean"]:text.append(f"| {row['group']} | {row['aMeanMs']:.6f} | {row['bMeanMs']:.6f} | {row['deltaMeanMs']:.6f} | {row['bCallsPerTick']:.3f} | {row['bExclusiveBytesPerTick']:.3f} |")
        text.append("")
    (directory/"COST_OF_KIAS.md").write_text("\n".join(text)+"\n",encoding="utf-8")
    return result


if __name__=="__main__":
    import sys
    report(Path(sys.argv[1]))
