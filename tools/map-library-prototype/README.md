# Map library developer prototype

## 같은 바이옴의 정확한 초기 지형 재현

`same_biome_prepare.py`는 새 실제 원본의 마지막 `PostMapInitialized` 관측을 별도로 묶는다. 같은 BiomeDef·같은 가로/세로 크기·빈 제품 params·4개 관측 sidecar가 있는 새 일회용 후보에서는 `replica_layer`를 선택한다. 원본 바닥의 모든 층·색·높이·동굴·비옥도·지붕·실제 암석/광석/건물의 고유 점유 및 보행 상태를 복제하고, 실제 Map Preview 두 색상 모드와 모든 raw 필드를 최종 대조한다. 다른 바이옴/크기에서는 복제 파일을 열지 않고 기존 적응 경로를 유지한다.

조건을 만족해도 원본에 무지지 지붕·unknown·필수 정보 누락이 있으면 정확 재현 후보를 제외한다. 이번 정해진 GL8 원본 중 CaveEntrance/SecludedValley2는 unsafe roof로 격리했고, 나머지6은 실제 최종13grid 전체와 고유 건물/PNG2가100% 일치했다. 원본8 전체 성공이나 범용 세이브 복제로 부르지 않는다. 식물·pawn·아이템·퀘스트·시간 변화의 재현은 범위 밖이고 제품 추천 UI·codec·Undo·설치 DLL에는 아직 연결하지 않았다. 기존 편집 지도/사용자 건물을 지우는 복제도 아니다. 외부 모드의 지연 callback이나 99999보다 늦은 source genstep까지 검증한 것은 아니다.

```powershell
# source-manifest.json의 capture_replica:true를 run.ps1 -WithGL로 새 소유 폴더에 생성/Archive한다.
python -X utf8 tools/map-library-prototype/same_biome_prepare.py --folder docs/analysis/2026-10-01-map-library-prototype/same-biome-v6 --source docs/analysis/2026-10-01-map-library-prototype/same-biome-v6/source-native-r1
# 준비된 same-biome-a/b/c manifest를 run.ps1로 새 폴더에 생성/Archive한다.
python -X utf8 tools/map-library-prototype/same_biome_report.py --help
```

원시 `*-terrain.json`/`*-geology.json`/`*-replica.json` 및 launch/cleanup/Player.log는 현장 검증 자료로 이 PC에 보존하고 Git에서 제외한다. Git에 저장된 exact 레시피만 내려받아서는 원본 snapshot이 없으므로 바로 실행할 수 없다. 새 환경에서는 source를 실제 생성하고 prepare로 경로/해시를 다시 연결해야 한다. [관측·보호 계약](same-biome-contract.md), [실제 결과와 한계](../../docs/analysis/2026-10-01-map-library-prototype/same-biome-v6/report.md), [실제 그림 HTML](../../docs/analysis/2026-10-01-map-library-prototype/same-biome-v6/same-biome-review.html).

## 동굴·자연 지붕·높이 이식 실험

`--details --caves`는 새 원본 `*-geology.json`을 요구한다. terrain-v2와 별개로 native working grid가 폐기되기 전99999에서 실제 높이·Caves·지붕 이름·통행 가능 여부·인공 바닥/건물을 캡처한다. 기존6종에 CaveEntrance와 SecludedValley를 더한8종을 새로 생성한다. 이미지나 지붕 개수 합계에서 동굴을 추측하지 않는다.

`cave.py`는 유한 높이/Caves와 None/얇은 자연 지붕/두꺼운 자연 지붕을 `cave_layer`로 저장한다. 불명 칸·원본 인공 지붕/바닥/건물은 이식 범위에서 제외한다. source199에서 실제 높이/동굴을 유지하고, 자연 지붕은 후반 native 생성 이후1601에서 보호/지지 조건을 확인한 뒤 적용한다. 현재 타일의 인공 지붕·건물·길·특수 물·기존 보호 대상은 그대로 둔다. 지붕 일괄 붕괴 처리나 원본을 맞추기 위한 건물 삭제는 하지 않는다.

`cave_report.py`는 원본 실제 배열을 직접 읽고 높이/Caves 오차, 동굴 마스크, 실제 자연 지붕·지붕 없는 칸, 걸어갈 수 있는 통로,4방향 연결/입구를 따로 대조한다. PNG 색이나 exporter 배열을 정답으로 쓰지 않는다. 이 검사와 기존 바위98%·전체 지원 자연 바닥95%·물/최종 보호 충돌을 모두 통과한 profile만 등록한다. 크기 변경은 최근접 원본 칸 대응이며 Caves 값 자체를 확대하지 않는다. 데이터/최종 audit이 없으면 명시적 동굴 이식은 거절하고 기존 일반 경로는 유지한다.

이 명시적인 전체 동굴 관측 실험은 네 종류의 칸별 sidecar를 정본으로 삼고 product params를 비운다. 근사 물 polygon을 중복 적용하면 제품 Authoring400이 원본 높이를 .3 이하로 낮추기 때문이다. polygon 변환은 손실·진단 SHA만 남기며 조각 수 제한도 기록한다. 제품의 빈 추천 허용으로 바꾸는 동작이 아니다. 제품 TileMapState·추천창·저장·Undo·설치 DLL에 아직 연결하지 않았고 source 자원 종류·spawn/사건/월드설정도 복제하지 않는다. 일반 rock-v4/이미지/기본 명령과 물의 기존 flatten 정책은 유지한다.8MiB 개발자 reader도 프로브에만 있으며 제품/모델 응답1MiB 제한을 변경하지 않는다.

```powershell
# source-manifest.json을 run.ps1 -WithGL로 새 소유 폴더에서 생성/Archive한 뒤:
python -X utf8 tools/map-library-prototype/cave_prepare.py --folder docs/analysis/2026-10-01-map-library-prototype/cave-v5 --source docs/analysis/2026-10-01-map-library-prototype/cave-v5/source-native-r2
# cave-a/b/c와 cave-controls manifest를 새 폴더에서 실제 생성/Archive한다.
python -X utf8 tools/map-library-prototype/cave_report.py --help
# 판정 실패 profile을 그대로 제외해 finalize하고 offline index를 만든 뒤:
# verify_caves.py --folder <folder> --runs <A> <B> <C> <controls>
```

실제 재현 폴더는 [동굴 비교 HTML](../../docs/analysis/2026-10-01-map-library-prototype/cave-v5/cave-review.html), [검증 영수증](../../docs/analysis/2026-10-01-map-library-prototype/cave-v5/verification.json), [결과·한계](../../docs/analysis/2026-10-01-map-library-prototype/cave-v5/report.md)에서 확인한다. 기술적 보존 검사는 미관·플레이 승인과 구분한다. 고도/Caves 일치만으로 통로·안전 지붕까지 승인하지 않으며, native 후반 고대 벽이나 무지지 지붕과 충돌한 조건은 제외한다.

## 물 위치까지 함께 가져오는 후속

`water.py`는 실제 원본 칸의 얕은 물/깊은 물/확인된 마른 땅을 별도 `water_layer`에 저장한다. 작은 연못도 생략하지 않는다. 개발자 `WaterPass`는 바닥 적용 전에 원본 수심/위치를 적용하고, 원본의 확인된 마른 곳에 있는 일반 연못만 정리한다. 원본의 건축 바닥은 마른 곳이라는 근거로만 사용하며 재료/건물을 복사하지 않는다. 대상의 강·바다·온천 및 연결된 물, 길·건축 바닥·건물과 불명 입력은 보호하고 충돌을 기록한다. 보통 습지 Marsh는 강/온천으로 오인하지 않는다.

## 칸별 바위·바닥 디테일 실험

`detail_prepare.py --folder <새 폴더>`는 기존 실제 GL 원자료를 해시와 함께 재사용하고 `build_catalog.py --details`로 명시적인 전체 구도 실험을 만든다. `rock.py`는 원본의 실제 자연 암석 점유와 비암석/불명 칸을 RLE `rock_layer`에 기록하며 작은 조각과 구멍도 생략하지 않는다. 원본 돌/광석 종류·자원량은 복제하지 않고 현재 타일의 기본 생성에 맡긴다. 전체 구도 경계 밖에 새로 생성된 광석은 정리될 수 있으므로 자원량 동일 보장은 아니다. 기존 바닥 정책은 유지하고, 이 명시적인 원본 데이터 이식만 지원하는 자연 바닥 조각을 1칸까지 저장한다. 이미지 입력은 추측하지 않으며 이전 12칸 기준을 유지한다.

[최종 보고서](../../docs/analysis/2026-10-01-map-library-prototype/rock-v4/report.md): Python79·실제 실행383·새58지도, 엄격한 원본 대조12/18. 실패한6조건은 그대로 제외하고 통과한 profile만 등록한다. `--details`의 native palette가 없으면 sidecar 없는 fallback으로 우회하지 않는다.

원본 M 마스크를 단순화한 윤곽과 비교하지 않고 원자료 전체와 직접 비교한다. 바위의 추가/누락·precision/recall/IoU≥98%와 원본의 지원 자연 바닥 전체를 분모로 한 같은 바이옴 재료 일치≥95%를 승인 기준으로 삼고 물·바닥 보호 검사도 함께 유지한다. 불명 영역과 보호 대상은 남기며 충돌 후보는 제외한다. 기존 부분 편집/sidecar 없는 생성 경로, 제품 DLL/추천창/저장/Undo는 변경하지 않는다. 동굴·지붕·원본 자원 배치·절차적 다양성은 별도 후속이다.

native `RocksFromGrid` 직전 단계199에서 점유 그리드를 맞추고 물403 뒤404에서 바위를 보정한다. 이후750의 `ScatterShrines`는 고대 건물 자리의 바위를 치우고 바닥을 덮을 수 있으므로 중간 적용 성공을 최종 원본 일치로 취급하지 않는다. 마지막 캡처에서도 실제 추가/누락과 보호 충돌을 따로 검사한다. 생긴 건물을 지우거나 주변 바닥을 덮어 원본 일치 수치를 높이지 않는다.

```powershell
python -X utf8 tools/map-library-prototype/detail_prepare.py --folder docs/analysis/2026-10-01-map-library-prototype/rock-v4
# 새 manifest로 run.ps1을 실행·Archive한 뒤 실제 원자료와 직접 비교한다.
python -X utf8 tools/map-library-prototype/rock_report.py --folder docs/analysis/2026-10-01-map-library-prototype/rock-v4 --runs detail-a-native-r2 detail-b-native-r2 detail-c-native-r2 rock-controls-native-r2
```

물 검사에서는 원본 전체 water mask(작은 조각 포함)와 직접 IoU≥98%를 요구한다. 현재 타일의 연못을 원본 mask에 합친 수치는 참고만 하며 후보 승인 근거로 쓰지 않는다. 보호 대상 충돌이나 water 근거 부재는 후보를 제외한다. `water_layer` 역시 제품 `TileMapState`/Preview/Undo/UI에는 미연결이고 완전한 저장소 구도에만 적용한다. 기존 부분 편집·강/해안/도로 타일 제외와 sidecar 없는 기존 생성 흐름은 유지한다.

실행: `water_report.py --folder docs/analysis/2026-10-01-map-library-prototype/water-v3 --prepare` → 새 transfer-a/b/c 및 water-controls manifest로 `run.ps1` 실제 생성/Archive → `water_report.py --folder .../water-v3 --runs <A> <B> <C> <controls>` → `finalize_catalog.py --folder .../water-v3` → `verify_water.py --folder .../water-v3 --runs <A> <B> <C> <controls>`. 보고서의 실제 폴더/실패 영수증을 확인하고, 재현 출력은 항상 새 폴더로 만든다. 원본 GL6개는 기존 실제 캡처를 해시로 대조해 재사용한다.

GL 원본 생성 → 관측된 산/물 윤곽을 기존 MapGenAI 명령으로 변환 → 로컬 의미 검색 → 격리된 실제 RimWorld 맵 생성의 실험이다. **제품 채팅 UI·이미지 입력·설치 DLL은 변경하지 않는다.**

## 실제로 구현한 범위

- 설치된 Geological Landforms의 44 XML 그래프를 조사하고 6종을 원래 GL worker로 생성한다. GL은 일반 MapGenAI 숫자 설정이 아니라 생성 그래프다.
- 원본 그래프/seed/타일/라이선스는 재현 자료로 보관한다. 다른 타일에 쓰는 명령에는 seed·biome·mutator·전역 보너스를 이식하지 않는다.
- 실제 바위/수심 데이터에서 기존 polygon/sub/add 명령을 만든다. 작은 조각은 40칸 또는 면적 0.15% 미만이면 생략하고 손실을 기록한다. 일반 바닥은 현재 바이옴 생성기를 유지한다. 오아시스만 국소 Soil/SoilRich를 포함한다.
- 이미지 경로는 **알려진 Map Preview 팔레트의 물 윤곽/수심**만 읽는다. 갈색 토양/바위/그림자 혼동으로 산을 만들어 넣지 않는다. 일반 사진·UI가 덮인 스크린샷·다른 팔레트에 대한 범용 비전은 아니다.
- `multilingual-e5-small` 로컬 CPU 임베딩, 조건 필터, 가족별 중복 제거, 코사인 검색/다양성 조정. 신경망 reranker·유료 API·신규 LLM 제안은 사용하지 않는다.
- 원본과 다른 월드의 250/300 크기·온대림/건조관목림/사막을 실제로 생성해 비교한다. 지원조건 밖·기존 월드 강/해안/도로가 있는 타일·부분 편집·정확한 방향/비율 요청은 저장소 후보를 반환하지 않는다.
- 실제 신규 지도와 동일 타일/seed의 빈 설정 baseline을 비교한다. PNG는 native Map Preview 색으로 그린 실제 전체 맵이다. 얼음 표면은 PNG에 남기고 지형 측정은 임시 얼음 아래 영구 Water를 읽는다.

## 바닥 이식 추가 (2026-10-01 후속)

[바닥 전후 비교 HTML](../../docs/analysis/2026-10-01-map-library-prototype/ground-v2/review.html)의 새 실험은 칸별 영구 TerrainDef/표면 이름을 캡처한다. 원래 prototype 결과와 파일을 분리하여 보존했다.

- `ground.py`: 흙/비옥한 토양/모래/부드러운 모래/자갈/진흙/습지/바위 바닥의 이름과 마스크를 별도 `ground_layer` RLE로 저장. 바닥 12칸 미만 조각·사용 불가/역할 미확인 지형을 생략하고 기록한다.
- `Probe.cs`: 단계405에서 기존 물·바위·길·건물·건축 바닥·높이와 미지정 칸을 보호하며 실제 바닥을 적용한다. 같은 바이옴은 정확한 재료, 다른 바이옴은 기본 토양/바위 바닥 유지·젖은 땅의 물가 제한·사막의 모래 해안 조정을 사용한다. 없는/위험한 바닥은 칠하지 않는다.
- `native-terrain-palette.json`: Map Preview의 실제 토양 색과 일반 색을 읽기만 한다. 이번 GL 격리 프로필의 실제 색347종/일반 색14종. 일반 색은 Soil/Gravel/MossyTerrain가 동일하여 이미지로 구분할 수 없다. 모호한 픽셀을 nearest-color로 강제 지정하지 않는다. Map Preview DLL/전역 색상표는 수정하지 않는다.
- `ground_report.py`: 원본의 실제 칸별 이름과 생성 결과를 대조한다. 실제 토양 색 입력과 일반 색 입력을 별도 검사한다. 원본/기존 이식/새 이식 HTML과 실제 그림을 만든다.
- `finalize_catalog.py`: 바닥 sidecar가 있는 후보는 바닥 검사까지 있어야 검색 가능 profile로 등록한다. geometry PASS만으로 바닥 성공을 주장하지 않는다.

**이 바닥 sidecar는 개발자 프로브만 해석한다.** 제품 `TileMapState`/추천 UI/저장/Undo에 아직 연결하지 않았다. 일반 제품 `ApplyPatches`만 호출하면 `params`의 윤곽만 적용되고 바닥은 누락된다. 바닥-only 개발자 대조군은 제품 추천을 우회해 제품 상태를 그대로 둔다. 제품의 빈 추천 거절 동작은 유지한다. 이미지 입력 OFF 정책도 유지한다.

```powershell
python -X utf8 -m unittest discover -s tools/map-library-prototype -p 'test_*.py' -v
dotnet build tools/map-library-prototype/PrototypeProbe.csproj --nologo
python -X utf8 tools/map-library-prototype/build_catalog.py --folder docs/analysis/2026-10-01-map-library-prototype/ground-v2 --source docs/analysis/2026-10-01-map-library-prototype/ground-v2/source-native-final
```

원본 v2 캡처가 없다면 기존 `source-manifest.json`을 `run.ps1 -WithGL`로 새 출력 폴더에서 다시 생성해야 한다. `run.ps1`은 소스가 DLL보다 새로우면 실행을 거절한다. 실제 replay와 바닥/윤곽 검사 전에 카탈로그를 확정하지 않는다. 전체 재현 순서·최종 명령·실제 결과는 새 보고서를 따른다.

## 실행

저장소 루트 `active/mapgen_ai`에서 실행한다. Python 패키지는 `requirements.txt`가 이 PC의 검증 버전이다. 모델 가중치는 저장소 밖 `F:/Projects/Rimworld/work/mapgenai-library-model-cache`에 내려받는다. 첫 모델 다운로드는 인터넷이 필요하며 게임의 의존성이 아니다.

```powershell
python -X utf8 -m unittest discover -s tools/map-library-prototype -p test_prototype.py -v
dotnet build tools/map-library-prototype/PrototypeProbe.csproj --nologo
```

카탈로그와 벡터를 다시 만들려면 보관된 원본 `*-terrain.json` 관측 자료가 필요하다. 원시 관측과 로컬 실행 로그는 Git에서 제외하며, 벡터 인덱스는 배포 모델이 아닌 소형 실험 자료다.

```powershell
python -X utf8 tools/map-library-prototype/build_catalog.py --folder docs/analysis/2026-10-01-map-library-prototype --source docs/analysis/2026-10-01-map-library-prototype/source-final
# 실제 run.ps1 재생 후 measure_transfer.py로 새 결과를 검사하고, 통과한 profile만 등록한다.
python -X utf8 tools/map-library-prototype/finalize_catalog.py --folder docs/analysis/2026-10-01-map-library-prototype
python -X utf8 tools/map-library-prototype/embedding.py --catalog docs/analysis/2026-10-01-map-library-prototype/catalog.json --index docs/analysis/2026-10-01-map-library-prototype/index
```

요청부터 실제 맵까지 연결하는 개발자 진입점:

```powershell
python -X utf8 tools/map-library-prototype/prepare_request.py --folder docs/analysis/2026-10-01-map-library-prototype --query '산 사이로 길게 열린 넓은 골짜기' --biome TemperateForest --world-seed library-user-example --output docs/analysis/2026-10-01-map-library-prototype/my-request.json
# 위 출력 cases > 0인 경우에만 실행. 출력 폴더는 새 경로여야 한다.
& tools/map-library-prototype/run.ps1 -Manifest docs/analysis/2026-10-01-map-library-prototype/my-request.json -Output docs/analysis/2026-10-01-map-library-prototype/my-request-run
# result.json이 생성되고 이 전용 게임이 종료한 뒤 같은 인자로 -Archive를 추가한다.
```

`run.ps1`은 기존 MAPGENAI_HEADLESS_OWNED 런타임과 F:의 일회용 프로필만 사용한다. DEV 기준 DLL SHA를 고정한다. 일반 게임·DEV 설치·사용자 설정은 수정하거나 종료하지 않는다. 기준 DLL이 바뀌면 검토 후 핀을 갱신해야 한다. GL 원본 재현은 source-manifest.json과 `-WithGL`을 사용하며 실험 프로필에만 Odyssey를 제외한다. 이를 사용자 DLC 조합 호환성으로 해석하지 않는다.

`--scope edit`, `--native-water`, `--native-roads`, 지원하지 않는 바이옴을 주면 저장소 후보가 없을 수 있다. 실제 사용자 타일을 자동 읽는 기능은 아직 UI에 연결하지 않았다. CLI의 biome/size/native-water 입력은 개발자 실험 조건이다. 작은 오아시스는 물 면적 오차로 현재 카탈로그에서 격리되어 반환하지 않는다.

## 읽어볼 산출물

- [실제 비교 HTML](../../docs/analysis/2026-10-01-map-library-prototype/review.html)
- [검증 보고서](../../docs/analysis/2026-10-01-map-library-prototype/report.md)
- [카탈로그](../../docs/analysis/2026-10-01-map-library-prototype/catalog.json)
- [검색 평가](../../docs/analysis/2026-10-01-map-library-prototype/retrieval-evaluation.json)
- [이미지 평가](../../docs/analysis/2026-10-01-map-library-prototype/image-evaluation.json)
- [윤곽 재생 평가](../../docs/analysis/2026-10-01-map-library-prototype/transfer-evaluation.json)

## 출처·권리

GL: m00nl1ght, [원본 저장소](https://github.com/m00nl1ght-dev/GeologicalLandforms), revision e8035e2b2fdb46ceb92aa159e17e72fdc5dcc421, 설치판 v1.7.13.1. 원본 라이선스 [CC BY-NC-SA 4.0](https://github.com/m00nl1ght-dev/GeologicalLandforms/blob/master/LICENSE). 이 실험의 GL 파생 윤곽 레시피·GL 지형 그림·카탈로그의 GL 자료는 같은 조건과 출처를 유지한다. 제품 기본 팩에 포함하거나 재배포한 것으로 취급하지 않는다. 일반 RimWorld 자산의 권리가 CC로 바뀌는 것도 아니다.

E5: [intfloat/multilingual-e5-small](https://huggingface.co/intfloat/multilingual-e5-small), MIT, revision 614241f622f53c4eeff9890bdc4f31cfecc418b3, 384차원. 공식 모델 카드의 `query:`/`passage:`·mean pooling·L2 규칙을 사용했다.

## 다음 제품 단계

미관 검토로 실제 사용할 사례를 선정하고, 여러 원본 변형을 추가한다. 그 뒤 DEV opt-in으로 기존 RecommendationPlan/Preview/UI에 검색 결과를 연결한다. 실제 현재 타일 생성의 성공·정착 가능한 공간을 확인한 후보만 보여주고 검색 실패 때 원래 모델 후보를 보존한다. 신규1+저장소최대2는 이전 설계 제안이며 이번 도구가 구현한 UI 동작은 아니다.
