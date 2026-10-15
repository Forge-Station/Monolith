"""Validate both real short FullGame runs, including repeated medical, DATA and docking waves."""
import json
from pathlib import Path
from fullgame_scenario import capabilities
from fullgame_report import tick_statistics, wave_statistics
from native_proof import read, lines, validate
from network_proof import validate as network


def report(root):
    root=Path(root)
    scenario=root/"quick-fullgame.jsonl"
    inputs=lines(scenario)
    required=set(capabilities()["required"])
    assert required<={item["type"] for item in inputs},"Quick omitted mandatory categories"
    runs=[]
    for variant in ("A","B"):
        path=root/f"quick-fullgame-{variant}"
        proof=validate(path,scenario)
        sessions=network(path)
        started=read(path/"started.json")
        assert started["measuredTicks"]==45000 and started["ships"]>=4 and sessions["clients"]==4
        for ship in {item["ship"] for item in inputs}:
            for kind in ("data.cut","data.restore","crew.critical","crew.recover","crew.death","crew.revive","dock.dock","dock.undock"):
                assert sum(item["ship"]==ship and item["type"]==kind for item in inputs)>=2,f"Missing repeated {kind}"
        runs.append(dict(variant=variant,path=str(path.resolve()),ships=started["ships"],native=proof,network=sessions,
            metrics=tick_statistics(path/"ticks.csv",started["measuredTicks"]),
            waves=wave_statistics(inputs,lines(path/"events.jsonl"),lines(path/"native-outcomes.jsonl"),started["ships"])))
    controls=read(root/"quick-controls/suite-status.json")
    assert len(controls["results"])==4 and all(item["status"]=="PASS" for item in controls["results"])
    room_controls=read(root/'quick-room-controls/suite-status.json')
    assert len(room_controls['results'])==2 and all(item['status']=='PASS' for item in room_controls['results'])
    result=dict(status="QUICK_A_B_PASS",fullABBA=False,mandatoryNativeCategories=sorted(required),runs=runs,
                controls=controls,roomControls=room_controls,scope="Real short A/B FullGame with four clients and repeated DATA/Crew/Dock; long ABBA remains separate")
    (root/"quick-report.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
    return result


if __name__=="__main__":
    import sys
    report(sys.argv[1])
