"""Compare actual native artifacts; never derives expectations from product sampling code."""
import argparse
import copy
import json
import gzip
from collections import Counter
from pathlib import Path

def read(folder, name):
    path=folder/name
    if path.exists(): return json.loads(path.read_text(encoding="utf-8-sig"))
    with gzip.open(str(path)+".gz","rt",encoding="utf-8-sig") as stream: return json.load(stream)

def unroll(terrain):
    return [value for count, value in terrain["layers"] for _ in range(count)]

def terrain_verdict(ta,tb,floor,legacy):
    """The same verdict function serves real maps and synthetic corruptions."""
    aa,bb=unroll(ta),unroll(tb)
    changed=[i for i,(x,y) in enumerate(zip(aa,bb)) if x!=y]
    sensitive=[i for i in changed if aa[i].split("|")[0] not in ("Soil","Sand")]
    outside=[i for i in changed if i not in floor]
    checks={"unchanged actual elevation":ta["elevationHash"]==tb["elevationHash"],
        "unchanged cave field":ta["cavesHash"]==tb["cavesHash"],
        "full terrain coverage":len(aa)==len(bb)==ta["width"]*ta["height"],
        "special surfaces and water unchanged":not sensitive,
        "no ground changes outside authored floor":not outside}
    if legacy: checks["exact full terrain layers"]=aa==bb
    return checks,changed,sensitive,outside

def plants_identical(a,b):
    # Coordinates, species and growth are the evidence, not a supplied summary hash.
    return [r[:4] for r in a["rows"]]==[r[:4] for r in b["rows"]]

def mutation_controls():
    base=dict(width=3,height=1,layers=[[1,"WaterShallow|WaterShallow|||"],[2,"Soil|Soil|||"]],elevationHash="same",cavesHash="same")
    inside={0,1}
    water=copy.deepcopy(base);water["layers"]=[[3,"Soil|Soil|||"]]
    outside=copy.deepcopy(base);outside["layers"]=[[1,"WaterShallow|WaterShallow|||"],[1,"Soil|Soil|||"],[1,"Sand|Sand|||"]]
    allowed=copy.deepcopy(base);allowed["layers"]=[[1,"WaterShallow|WaterShallow|||"],[1,"Sand|Sand|||"],[1,"Soil|Soil|||"]]
    plants={"rows":[[1,2,"Plant_Grass",0.5]]}
    species={"rows":[[1,2,"Plant_TreeOak",0.5]]};position={"rows":[[2,2,"Plant_Grass",0.5]]}
    return {"water to soil is rejected by production terrain verdict":not terrain_verdict(base,water,inside,False)[0]["special surfaces and water unchanged"],
        "outside-floor soil change is rejected by production terrain verdict":not terrain_verdict(base,outside,inside,False)[0]["no ground changes outside authored floor"],
        "permitted inside-floor surface change passes regular comparison":all(terrain_verdict(base,allowed,inside,False)[0].values()),
        "the same allowed surface change fails strict legacy equality":not terrain_verdict(base,allowed,inside,True)[0]["exact full terrain layers"],
        "plant species mutation fails strict plant comparison":not plants_identical(plants,species),
        "plant coordinate mutation fails strict plant comparison":not plants_identical(plants,position),
        "unchanged plants pass strict comparison":plants_identical(plants,plants)}

def compare(before, after, legacy=False):
    a, b = read(before, "fixture.json"), read(after, "fixture.json")
    checks=[]
    def check(ok, name): checks.append(dict(ok=bool(ok), name=name))
    for name in ("fixture", "tile", "worldSeed", "mapSize", "biome", "hilliness", "worldLinks"):
        check(a[name] == b[name], "Same fixture " + name)
    for name in ("setupRandSeed","ticksAbs","ticksGame","gameStartAbsTick","meanTileTemperature"):
        if name in a or name in b: check(a.get(name)==b.get(name),"Same controlled input "+name)
    states=[]
    for folder in (before,after):
        state=read(folder,"state.json")
        # Codec JSON uses elevation_shapes (never ignore other state fields).
        state_body=state.get("state",state)
        for shape in state_body.get("elevation_shapes",state_body.get("elevationShapes",[])):
            shape.pop("details",None)
        states.append(state)
    check(states[0]==states[1],"Same complete authored state excluding details only")
    for folder in (before,after):
        r=read(folder,"result.json")
        check(r["ok"],folder.name+" native probe passed")
        check(r["newProviderCalls"]==0 and r["blockedProviderFactoryCalls"]==0,folder.name+" no provider attempted")
    metrics={}
    for phase in ("preview","full"):
        ta,tb=(read(folder,phase+"-terrain.json") for folder in (before,after))
        floor=set(read(after,phase+"-floor-mask.json"))
        verdict,changed,sensitive,outside=terrain_verdict(ta,tb,floor,legacy)
        for name,ok in verdict.items(): check(ok,phase+" "+name)
        p,q=(read(folder,phase+"-plants.json") for folder in (before,after))
        apos={(r[0],r[1],r[2]) for r in p["rows"]};bpos={(r[0],r[1],r[2]) for r in q["rows"]}
        metrics[phase]=dict(changedGroundCells=len(changed),specialSurfaceChanges=sensitive,outsideChanges=outside,
            beforePlants=p["count"],afterPlants=q["count"],sharedPlants=len(apos&bpos),removedPlants=len(apos-bpos),addedPlants=len(bpos-apos),
            beforeSpecies=dict(Counter(r[2] for r in p["rows"])),afterSpecies=dict(Counter(r[2] for r in q["rows"])),
            beforeUnexpectedSpecies=len(p["unexpectedSpecies"]),afterUnexpectedSpecies=len(q["unexpectedSpecies"]),
            beforeBelowNativeFertilityMinimum=len(p["belowNativeFertilityMinimum"]),afterBelowNativeFertilityMinimum=len(q["belowNativeFertilityMinimum"]))
        # Native spawning can place feature plants not in BiomeDef.AllWildPlants. Preserve counts as evidence.
        # Positive density-weight behavior is checked by the probe's actual Plants900 hook, not by guessing counts.
        if legacy:
            check(plants_identical(p,q),phase+" old/new null exact plant coordinates, species and growth")
    if legacy:
        p,q=(read(folder,"full-plants-complete.json") for folder in (before,after))
        check(plants_identical(p,q),"Old/new null complete map plants identical after generation scope")
        p,q=(read(folder,"full-plants900.json") for folder in (before,after))
        check(plants_identical(p,q),"Old/new null initial Plants900 coordinates species growth identical")
        metrics["initialPlants900"]={"before":p["count"],"after":q["count"],"identical":plants_identical(p,q)}
    elif a["fixture"] == "D":
        r=read(after,"result.json")["results"]
        check(r["fullPlantsScope"]["activeWeights"]==0,"Desert native density weighting unchanged")
    # Same detector must reject synthetic corruption in this run before a zero-change verdict is trusted.
    for name,ok in mutation_controls().items(): check(ok,"Positive control: "+name)
    return dict(ok=all(c["ok"] for c in checks),before=str(before),after=str(after),legacy=legacy,checks=checks,metrics=metrics,
        note="Controlled native fixtures. Plant count or visual preference is not universally guaranteed; special-biome/native feature plant exceptions are reported, not silently excluded.")

if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("before",type=Path);parser.add_argument("after",type=Path);parser.add_argument("--legacy",action="store_true");parser.add_argument("--out",required=True,type=Path)
    args=parser.parse_args()
    if args.out.exists(): raise SystemExit("Refusing to overwrite existing evidence: "+str(args.out))
    result=compare(args.before,args.after,args.legacy);args.out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps(dict(ok=result["ok"],checks=len(result["checks"]),failed=[c["name"] for c in result["checks"] if not c["ok"]],metrics=result["metrics"]),ensure_ascii=False))
    raise SystemExit(0 if result["ok"] else 1)
