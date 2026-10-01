# 바닥 종류 보존·미리보기 팔레트 후속 프로토타입

2026-10-01 · 기준 dev a0bc390 · 개발자 도구만 변경. 제품 UI·이미지 입력 OFF·DEV/일반판/dist DLL·사용자 설정은 그대로다.

## 문제와 변경

이전 이식은 산/물 윤곽을 읽고 일반 바닥을 G로 합쳐 현재 바이옴의 바닥을 남겼다. source v2 관측은 칸별 영구 TerrainDef와 보이는 표면을 분리한다. `ground.py`는 실제 바닥 이름과 마스크를 별도 RLE `ground_layer`에 기록한다. Histogram만 있는 옛 캡처는 바닥 복원 근거로 쓰지 않는다.

개발자 `GroundPass`는 생성 단계405에서 같은 바이옴의 실제 바닥을 적용한다. 기존 물/강/바위/높이/건물/건축 바닥/도로와 미지정 칸은 보호한다. 다른 바이옴은 기본 토양·바위 바닥을 유지하고 젖은 바닥을 실제 물가에 제한한다. 숲의 호숫가를 사막으로 옮기면 진흙/습지를 모래로 조정하고 넓은 흙밭/비옥한 토양을 암묵적으로 추가하지 않는다. 없는/위험한 바닥은 건너뛴다. 이 조정은 지원한 바이옴/원본의 규칙이며 전 바이옴의 생태학적 모델이 아니다.

Map Preview의 실제 지형 색347종/일반 색14종을 읽었다. 외부 DLL/전역 색상표는 변경하지 않았다. 같은 색의 서로 다른 바닥, 섞인 그림자, 없는 색을 가장 가까운 재료로 강제 지정하지 않는다. 일반 색의 Soil/Gravel/MossyTerrain는 같은 RGB라 색상표를 늘려도 원래 종류를 복구할 수 없다. 직접 데이터는 미리보기 색이 없어도 바닥 이름을 읽을 수 있다.

## 새 검증

- `python -X utf8 tools/map-library-prototype/verify_ground.py`: Python 행동 검사 **37/37**, 프로브 빌드 경고/오류0, 최종 native 실행 검사 **306/306**, 실제 맵 **70개**(원본6+이식27+baseline27+양성/별도 입력5+baseline5). 최종 유료 API/Claude/Fable 호출0.
- `ground_report.py --folder .../ground-v2 --runs transfer-a-native-final transfer-b-native-final transfer-c-native-final extra-native-r3`: 바닥 적용26/26에서 보호 대상/미지정 칸/높이 변경0. 같은 바이옴16조건의 비교 가능한 원본 바닥 이름 일치 **98.6180~100%**. 전체 지도의 복사율/보편적 정확도/미관 점수가 아니다. 물·바위·건축 바닥·생략한 작은 조각과 지형 자체가 달라진 칸은 이 바닥 비교에서 제외한다.
- 선언한 source 바닥11종 모두 실제 적용 계수>0: Granite_Rough, Gravel, Limestone_Rough, Marble_Rough, Mud, Sand, Sandstone_Rough, Slate_Rough, SoftSand, Soil, SoilRich. 로드된 지형347종을 모두 이식 검증했다는 뜻은 아니다.
- 실제 토양 색의 held-out GL Lake 이미지: 확실하다고 판정한 바닥 이름 precision100%, unknown7.0608%. 원본 labels는 팔레트 구성에 쓰지 않았다. 일반 색 이미지: 확실한 일부 바닥만 precision100%, **unknown72.832%**. 동일색 흙/자갈/이끼를 구분했다고 주장하지 않는다. 사진/JPEG/UI/다른 팔레트/색상 모드 혼합은 미검증이다.
- 실제 양성 대조군 `ground-guard`: 물1·도로1·건축 바닥2·edifice107·높은 고도99가 보호 범위에 존재, 적용 중 변경0. 주변 바닥9587칸은 바뀌어 보호 검사가 빈 대조군에서 통과한 결과가 아님을 확인했다.
- `missing-ground`/`unsafe-ground`: 미설치 TerrainDef/WaterDeep을 일반 바닥으로 지정. 각각9686/9643칸의 실제 처리 대상이 있어도 칠한 칸0, 최종 전체 TerrainDef가 동일 타일의 빈 설정 baseline과 전부 같다.
- 사막 호숫가: 바닥4523칸 적용, 진흙→모래296칸 조정, 기본/부적합한 이식40612칸 유지. 바닥 적용 중 보호 대상 변경0.
- 바닥을 추가하지 않은 core-foothills/core-dry-clearing × 기존3조건: 이전 프로토타입과 모든 topology 칸·named terrain histogram·전체 PNG 바이트가 **6/6 동일**.
- 윤곽/물 재검사: 전체27중24 통과, source 비교21중18 통과. 기존 작은 오아시스3조건의 물 면적 IoU 실패는 유지되어 검색 profile에서 격리했다. 바닥 검사만 성공했다고 추천 가능으로 바꾸지 않았다.
- [검증 영수증](verification.json), [바닥 검사](ground-evaluation.json), [윤곽 검사](transfer-evaluation.json), [비교 HTML](review.html), [직접 표시한 그림](comparison.png). HTML의 PNG24개 디코드 검사 통과. 브라우저 렌더는 기존 로컬 자동열기 제한을 우회하지 않아 미검증이다.

## 재현 순서

제품 repo 루트에서 실행한다. `run.ps1`은 기존 소유 표시가 있는 격리 런타임/전용 프로필만 사용하며 성공한 프로브 빌드가 필요하다. baseline DEV DLL SHA는 verification.json과 대조한다. 각 출력 폴더는 새 경로로 만들고 전용 프로세스 종료 후 같은 인자의 `-Archive`로 정리한다. 다른 게임을 종료하지 않는다.

1. `dotnet build tools/map-library-prototype/PrototypeProbe.csproj --nologo`
2. 기존 `source-manifest.json`으로 `run.ps1 -WithGL` → source-native-final의 v2 캡처. GL 원본은 겹치는 graph 때문에 전용 프로필에서 Odyssey를 제외한다. 사용자 모드/DLC 설정은 바꾸지 않는다.
3. `build_catalog.py --folder docs/analysis/2026-10-01-map-library-prototype/ground-v2 --source docs/analysis/2026-10-01-map-library-prototype/ground-v2/source-native-final`
4. 생성된 transfer-a/b/c-manifest.json으로 실제 native replay. 각9사례와 같은 타일 baseline을 캡처한다.
5. `ground_report.py --folder .../ground-v2 --prepare-extra` → 일반 색 이미지/사막/없는 바닥/위험한 바닥/실제 보호 대상5사례. extra-manifest.json을 replay한다.
6. `measure_transfer.py --folder .../ground-v2 --runs transfer-a-native-final transfer-b-native-final transfer-c-native-final`
7. `ground_report.py --folder .../ground-v2 --runs transfer-a-native-final transfer-b-native-final transfer-c-native-final extra-native-r3`
8. `finalize_catalog.py --folder .../ground-v2` → geometry+ground 근거가 있는 profile만 등록. 이후 `embedding.py --catalog .../ground-v2/catalog.json --index .../ground-v2/index`로 로컬 E5 인덱스 재생성. 모델은 기존 F: 캐시를 사용하며 API 호출이 아니다.
9. `verify_ground.py` → 새 tests/build/hash/실제 결과/HTML decode/이전6지도 동일성 검사. 중간 실패를 새 PASS로 덮어쓰지 않는다.

## 실패·한계

- 중간 source/transfer r1/r2와 최종 native-final을 구분한다. 작은 조각은 바닥12칸/윤곽40칸 또는0.15% 기준으로 생략하고 영수증에 기록했다.
- extra-native-final은2사례 뒤 빈 params를 제품 추천 검증에 넣어 정상 "Recommendation has no changes" 거절로 실패했다. 제품 수정 없이 바닥-only 개발자 대조군의 상태 불변 경로를 추가했다. extra-native-r2는 내부 `Values` 접근의 빌드 실패 뒤 구 DLL이 실행돼 같은 실패였다. 공개 `Keys`로 수정하고 소스가 DLL보다 새로우면 launch를 거절하도록 했다. 최종 extra-native-r3는5/5 실행·41검사 통과다.
- 기존 무창 런타임의 Plant.Print→GetSnowDepth 렌더 경고는 이전 transfer-b-native 로그에도 있다. actual native PNG/지형 관측과 무관한 렌더 정리 경고이며 이번에 해결했다고 주장하지 않는다. 무경고 실제 플레이 검증은 아니다.
- **ground_layer는 개발자 probe만 적용한다.** 제품 TileMapState/추천 UI/미리보기/Undo/저장에는 연결하지 않았다. ApplyPatches만 호출하면 params의 윤곽만 적용한다. 카탈로그에 이 runtime contract를 명시하고 새 카탈로그를 옛 원본 자료와 분리했다.
- 기존 강/해안/월드 도로 타일·부분 편집·정확한 방향/비율 제외는 그대로다. 원본의 절차적 변형/동굴/자원/이벤트는 이식하지 않는다. 기존 사용자 기능은 이번 작업에서 코드를 바꾸지 않았으며 전체 자연어 재채점은 하지 않았다.
- 소유 프로브11개 정리, 다른 게임·사용자 프로필·설치4 DLL·제품 source/dist 불변 확인. 공유 메모리 쓰기는 사용자 요청이 없어 state/log에 근거만 남겼다.

## 출처

GL 원본/파생 레시피/RLE 바닥 패턴·지형 그림의 출처/CC BY-NC-SA4.0 조건은 [상위 귀속 문서](../ATTRIBUTION.md)를 유지한다. 이 폴더의 GL 파생 데이터/그림도 같은 조건이다. 검사 전용 guard/없는 바닥/위험한 바닥 레시피는 MapGenAI의 독립 검사 자료다. 게임 자산의 권리를 바꾸는 선언이 아니다. 새 모델 가중치·게임/GL DLL은 Git/모드 배포에 포함하지 않는다.
