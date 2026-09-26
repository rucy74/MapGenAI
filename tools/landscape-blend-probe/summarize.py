import json
from pathlib import Path
from compare import read, plants_identical, mutation_controls

root=Path(__file__).resolve().parents[2]/"docs/analysis/2026-09-23-landscape-blending"
primary=["a-null-old","a-null-new","a-natural","b-natural","c-natural","d-natural","protected-natural","road-natural"]
quiet=["a-null-old-quiet","a-null-new-quiet","a-null-old-quiet-repeat","a-natural-quiet"]
runs=[]
for names,suffix in ((primary,"s4-r5"),(quiet,"s4-r7")):
    for name in names:
        folder=root/("native-blend-"+name+"-"+suffix)
        result=read(folder,"result.json");launch=read(folder,"launch.json");fixture=read(folder,"fixture.json")
        runs.append(dict(run=folder.name,ok=result["ok"],checks=len(result["checks"]),failures=[c["name"] for c in result["checks"] if not c["ok"]],sourceDllSha256=launch["sourceDllSha256"],probeDllSha256=launch["probeDllSha256"],fixture=fixture,results=result["results"]))

old=root/"native-blend-a-null-old-quiet-s4-r7";new=root/"native-blend-a-null-new-quiet-s4-r7";repeat=root/"native-blend-a-null-old-quiet-repeat-s4-r7"
initial_a,initial_b,initial_repeat=(read(p,"full-plants900.json") for p in (old,new,repeat))
final_a,final_b,final_repeat=(read(p,"full-plants.json") for p in (old,new,repeat))
legacy_compare=read(root,"native-compare-legacy-quiet-compressed.json")
natural_compare=read(root,"native-compare-natural-quiet-compressed.json")
prefix=next(r for r in runs if r["run"]=="native-blend-a-natural-quiet-s4-r7")["results"]["fullNativeDensitySamples"]

retained=[]
final_names={r["run"] for r in runs}
reasons={
    "native-blend-a-none-s3-r1":"Instrumentation failure: product response JSON serializer was too small for full plant audit. Native step caught probe exception; not a product verdict.",
    "native-blend-a-none-s3-r2":"Instrumentation failure: System.Web.Extensions serializer was absent in native Mono. Owned failed process stopped after path verification.",
    "native-blend-b-none-s4-r3":"Fixture failure before map generation: river tiles were incorrectly required to have no mutators.",
    "native-blend-b-natural-s4-r4":"Measurement failure: temporary ThinIce was mistaken for removed river; raw underlying river layers were retained. r5 uses underlying water and fixed date.",
    "native-blend-protected-natural-s4-r4":"Interrupted while replacing the uncontrolled batch with the requested fixed eight-run batch; no completed result.",
    "native-blend-a-null-old-quiet-s4-r6":"Excluded instrumentation launch: copied previous probe while new build was pending, so QuietStructures was not applied. EXCLUDED.txt retained."
}
for directory in sorted(root.glob("native-blend-*")):
    if not directory.is_dir() or directory.name in final_names: continue
    result=read(directory,"result.json") if (directory/"result.json").exists() else None
    retained.append(dict(run=directory.name,ok=None if result is None else result["ok"],reason=reasons.get(directory.name,"Earlier uncontrolled-date diagnostic; retained, not used as the final deterministic comparison.")))

summary={
    "scope":"Native controlled disposable game and MapPreview verification; product branch dev-natural-landforms, commit 4b931b4e782d097429b20b8635604bbd4cfd4f0e; no install, provider or Fable calls.",
    "nativeAssertions":{"runs":len(runs),"passed":sum(r["checks"]-len(r["failures"]) for r in runs),"failed":sum(len(r["failures"]) for r in runs)},
    "legacyControlled":{
        "conditions":"A organic open basin with shallow pond; details null, fixed world/setup RNG/date; ruinDensity=0, dangerDensity=0 explicitly recorded in both states.",
        "previewTerrainLayersIdentical":read(old,"preview-terrain.json")["terrainHash"]==read(new,"preview-terrain.json")["terrainHash"],
        "fullTerrainLayersIdentical":read(old,"full-terrain.json")["terrainHash"]==read(new,"full-terrain.json")["terrainHash"],
        "fullElevationIdentical":read(old,"full-terrain.json")["elevationHash"]==read(new,"full-terrain.json")["elevationHash"],
        "fullCavesIdentical":read(old,"full-terrain.json")["cavesHash"]==read(new,"full-terrain.json")["cavesHash"],
        "initialPlants900":{"oldCount":initial_a["count"],"newCount":initial_b["count"],"coordinatesSpeciesGrowthIdentical":plants_identical(initial_a,initial_b)},
        "finalPlants":{"oldCount":final_a["count"],"newCount":final_b["count"],"coordinatesSpeciesGrowthIdentical":plants_identical(final_a,final_b),"strictVerdict":"FAIL retained"},
        "sameFrozenDllRepeat":{"initialPlants900Identical":plants_identical(initial_a,initial_repeat),"finalPlantsIdentical":plants_identical(final_a,final_repeat),"finalCounts":[final_a["count"],final_repeat["count"]]},
        "interpretation":"Observed equality at terrain and initial Plants900, with later differences also observed when repeating the frozen DLL. The exact later-stage cause is not established. Do not claim full final-map plant equality.",
        "strictComparisonFailedChecks":[c["name"] for c in legacy_compare["checks"] if not c["ok"]]
    },
    "naturalQuietComparison":{"ok":natural_compare["ok"],"checks":len(natural_compare["checks"]),"metrics":natural_compare["metrics"]},
    "actualNativeDensityPrefix":{"verified":len(prefix)==5 and all(s["ok"] for s in prefix),"samples":prefix},
    "mutationControls":mutation_controls(),"runs":runs,"retainedDiagnostics":retained,
    "compression":read(root,"native-compression.json"),
    "limitations":["All foundation masks in these fixtures contained zero cells; foundation protection is not independently exercised here.","Full plant coordinates after late generation are not exactly reproducible across the observed frozen-DLL repeats; strict failures remain visible.","The native species list audit reports the native Anima tree as an exception. Raw fertility-minimum observations include native special plant rules and are not habitat violation verdicts.","MapPreview PNGs contain terrain, not rendered grass or trees. Actual full-map plant coordinates are separate JSON.gz artifacts.","Only these official-DLC controlled fixtures were exercised; no all-biome/all-mod or visual-preference claim."]
}
summary["compression"]={k:v for k,v in summary["compression"].items() if k!="entries"}
(root/"native-summary.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")

lines=["# Landscape blending native 검증", "", "제품 `4b931b4e782d097429b20b8635604bbd4cfd4f0e`, s4 DLL `6d854073d57fc6a20e8735aeae7a49cb13d481cdadc5061f5ab34d841b92285e`. 이전 DLL은 `fd1327816da1a171f347bed08577cd7aac27d898954d70edb0b8104b93cfd284`.","",
       f"최종 고정 8회와 제한된 진단 4회에서 native 단계 검사 **{summary['nativeAssertions']['passed']} PASS / {summary['nativeAssertions']['failed']} FAIL**. 별도의 최종 식물 완전동일 비교는 2개 FAIL을 유지한다. 이 둘을 합쳐 무조건 완료로 판정하지 않는다.","",
       "## 실제 적용 및 보호", "", "| 고정 사례 | 바닥 변경 | 가중치 적용 셀 | 최종 실제 식물 | 물 / 자갈 / 도로 / 전체 채움 보호 대상 |", "| --- | ---: | ---: | ---: | --- |"]
for r in runs:
    if not r["run"].endswith("s4-r5") or "fullBlend" not in r["results"]:continue
    b=r["results"]["fullBlend"];s=r["results"]["fullPlantsScope"];f=r["results"]["fullFinal"]
    lines.append(f"| {r['fixture']['fixture']} | {b['changed']} | {s['activeWeights']} | {f['plants']} | {b['protectedWaterCells']} / {b['protectedGravelCells']} / {b['protectedRoadCells']} / {b['protectedFillCells']} |")
lines += ["", "각 직접 blend 전후 검사에서 물·산·자갈·도로·명시적 채움 전체 영역·특수 지면·영역 밖·높이 변경 위반은 0이었다. 보호 fixture는 32,991개 eligible 셀 중 23,094개를 선택하여 70%를 유지했다. 로컬 도로 footprint 572칸도 보존됐다. foundation 대상은 모든 fixture에서 0칸이므로 실제 foundation 보존까지 검증했다고 말하지 않는다.", "",
          "사막은 208개 실제 식물과 모든 계획 weight=1을 기록했다. 온천과 강은 기본 생성기에 의해 실제 생성됐다. B의 원래 물 9,676칸은 강 9,082칸(얕은 강 4,560 + 가슴 깊이 강 4,522)과 얕은 물 594칸의 합이다. 수면 검사에는 임시 얼음 아래 원래 물도 포함한다.","",
          "활성화된 실제 Soil 5셀에서 `GetDesiredPlantsCountAt(cell, .25)` 결과가 native 기본식×weight와 맞았고, prefix 없는 기본값 0.1625와 달랐다(예: weight 0.929583848 → 실제 0.151057363). 단순 계획 필드 검사뿐 아니라 호출 결과를 확인했다. 생성 scope 종료 후 Weight는 1이다.","",
          "## 기존 상태 보존: 입증한 범위와 남은 차이", "",
          "유적/위협 밀도를 명시적으로 0으로 둔 quiet 대조에서는 이전 DLL과 새 DLL의 preview 및 full-map 62,500칸 전체 지면층·elevation·caves가 동일했다. Plants900 직후 **23,321개 식물의 좌표·종·성장도도 완전히 동일**했다.", "",
          "그러나 최종 식물은 이전 22,479개, 새 버전 22,493개로 달라 strict 비교 2개는 FAIL이다. 같은 frozen DLL을 한 번 더 실행해도 초기 23,321개는 같고 최종은 22,490개로 달랐다. 따라서 차이가 초기 식생 이후 단계에서 관측됨을 확인했으며, 정확한 뒤 단계 원인은 확정하지 않았다. 최종 식물까지 완전 동일하다고 주장하지 않는다.","",
          "유적을 켠 기존 대조에서도 같은 none 설정의 반복에 유적 바닥·최종 식물 차이가 관측됐다. 해당 strict 실패는 원자료에 보존했다. 판정 조건을 완화해 통과시키지 않았다.","",
          "quiet null→natural은 preview/full 각각 **347칸 변경**, 보호 위반 0으로 독립 비교 34개 PASS. 식물 최종 수는 22,493→22,578이지만 최종 수 차이를 전부 새 기능의 순효과로 단정하지 않는다.","",
          "## 그림과 원자료", "",
          "- [기존 디테일 없음](native-blend-a-null-new-s4-r5/preview.png) / [자연 디테일](native-blend-a-natural-s4-r5/preview.png)",
          "- 각 실행 폴더의 `full-plants.json.gz`: 실제 식물 좌표·종·성장도·지면·바이옴·거리. `full-vegetation-plan.json.gz`: 계획 후보 셀·weight·거리.",
          "- `native-summary.json`: DLL/probe 해시, 사례별 결과, 초기와 최종의 분리 판정. `native-compression.json`: 압축 전 원문 SHA256와 완전 왕복 검증.",
          "- 큰 JSON 191개를 186,459,948바이트에서 31,149,640바이트로 무손실 압축했다. 압축 후 같은 비교기를 다시 실행해 성공 비교34개 PASS 및 legacy의 동일한 strict2FAIL을 확인했다.","",
          "`compare.py` 실제 판정 함수에 물→토양, 영역 밖 지면, 식물 좌표·종 변조를 넣은 부정 대조와 허용 변화/동일 식물의 양성 대조 7개가 통과했다. 작은 result/fixture/launch와 PNG는 그대로다.","",
          "## 실패·제외 기록", ""]
for d in retained:
    if d["run"] in reasons:lines.append(f"- `{d['run']}`: {d['reason']}")
lines += ["", "모든 실제 실행은 표시된 owned game copy와 일회용 프로필에서 순차 수행했다. 현 설치나 원본 F:/G:는 수정하지 않았고 API/Claude/Fable 호출은 0이다. 전체 바이옴·모드 조합, 사용자 미관 평가, 미리보기의 식물 표시까지 검증한 것은 아니다."]
(root/"native-report.md").write_text("\n".join(lines)+"\n",encoding="utf-8")
print(json.dumps({"nativeAssertions":summary["nativeAssertions"],"initialPlants900":summary["legacyControlled"]["initialPlants900"],"actualPrefix":summary["actualNativeDensityPrefix"]["verified"],"naturalQuietComparison":summary["naturalQuietComparison"]["ok"]}))
