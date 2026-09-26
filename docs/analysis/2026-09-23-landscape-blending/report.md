# 자연지형의 주변 바닥·초기 식생 연결 — DEV

## 변경과 적용 범위

짧은 요청으로 만든 자연 분지·골짜기·산기슭 평야가 주변과 더 잘 이어지도록,
이미 생성된 물과 산 경계를 읽어 작은 모래/자갈 구간과 초기 식생 밀도 변화를 더한다.
자동 비옥토 원판이나 새 호수를 덧붙이는 기능은 아니다. 물과 특징을 추가하는 기존 도구를
계속 사용하며, 자동 연결은 새 육상 자연지형의 평지 안에서만 작동한다.

- `details:natural`은 새 `shape_ops add`의 기본값이다. 저장된 null/none은 기존 동작을 유지한다.
  부분 편집은 값을 보존하고 `none`으로 끌 수 있다. 전체 배열로 새 지형을 제안할 때는 명시해야 한다.
- stage420에서 실제 물/산까지의 거리를 계산한다. 일반 Soil/Sand만 편집하며 기존 Gravel은
  native 아스팔트 도로 어깨와 구별할 수 없어 전부 보존한다.
- 명시 도형·재료·비율 채움의 전체 원본 영역·도로 footprint·기초·건물·물·특수 지형은 제외한다.
  높이와 영역 마스크는 변경하지 않는다. 따라서 70% 채움의 남은 30%도 자동 바닥 변경에서 빠진다.
- Plants900 동안만 지역 밀도 가중치를 사용한다. 바이옴 식물 종류/성장 가능 조건과 사막·랜드마크의
  별도 밀도 규칙을 교체하지 않으며 이후 재성장은 native 경로다. 효과를 켠 맵에서 생성 RNG 소비와
  전체 포화도가 달라질 수 있으므로 편집 영역 밖 식물의 개별 좌표까지 보존한다는 뜻은 아니다.
- Map Preview는 식물 배치를 생략한다. 미리보기 바닥과 실제 완성 맵 식물을 별도로 관측한다.
  지형 생성에 별도 모델 단계를 추가하지 않았다. 이번 검증은 API를 호출하지 않는다.

변경 코드: `LandscapeBlendField`, `LandscapeBlendGeneration`, `LandscapeVegetationPatch`,
`AuthoringGenerationPatch`와 details 저장/편집/한영 설명/프롬프트 경로.
[사용자 테스트 문장](manual-tests-ko.md), [원래 계획](plan.md).

## 복구 기준과 최종 검증

작업 전 local commit `d84ea4bfd319ec36708d66aaf0148cae8601eb71`, tag
`dev-before-landscape-blending-2026-09-23`. 구현 commit은 `4b931b4`다. 일반 배포는 보존한다.
이하 native 실행은 2026-09-24에 수행했고, 2026-09-26에 같은 DLL·소스와 원자료를 확인하여
원본 DEV 통합/설치를 재개했다. 원본 이관 시 cherry-pick으로 commit ID가 달라질 수 있다.

제품 s4 SHA256: `6d854073d57fc6a20e8735aeae7a49cb13d481cdadc5061f5ab34d841b92285e`.

- `dotnet build dev/Source/MapGenAI.csproj --no-restore -m:1 -p:UseSharedCompilation=false --nologo -v:q`:
  오류0, 기존경고2. 패키지 취약성 feed 접근 실패와 기존 assembly wildcard 버전 경고다.
- `dotnet build dev/Source/Tests/TextToMap.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false --nologo -v:q -o <workspace>/mapgenai-blend-tests-s4`
  후 해당 DLL 실행: **261 PASS / 0 FAIL**. 100개 무필터 field variant에서 경계·반복성·변형 다양성·
  건조 바이옴·가중치 상한·기존 기하 동일성을 검사한다. 원본 응답 회귀는 새 nullable 필드만 정규화했다.
- 독립 adversarial review: 도로 Gravel 보호 누락을 발견하고 s4에서 해소를 재확인했다.
  해당 5개 순수 검사도 독립 재실행해 PASS했다. [검토 기록](independent-review.md).
- 고정조건 r5의 8회 native 실행은 **128검사 PASS**. 물가/강/온천/사막/70% 채움/도로 fixture를
  실제 게임과 Map Preview에서 생성하고, 직접 blend 전후 모든 terrain layer와 높이/보호 마스크를
  대조했다. 물·산·전체 채움 영역·도로·기존 Gravel/foundation·특수 지형의 직접 변경은0이다.
  단, 실제 foundation 대상은0칸이어서 그 항목은 코드 보호 조건만 확인한 것이다.
- 제한된 진단4회를 포함한 최종12회에서 **185 native 검사 PASS**, 활성 효과의 독립 비교34개 PASS.
  최종 식물의 strict 비교2개 FAIL은 별도 보존했다. 해당 12회에서 생성 예외는 없었고 Mono의
  fallback library 메시지3줄은 frozen과 새 DLL 모두에 있었다.
- 유적/위협 밀도0의 별도 r7 대조에서 frozen d84ea4b와 새 null 설정의 Preview/완성 맵 지형
  62,500칸 모든 층과 height/cave가 같고 **Plants900 직후 식물23,321개 좌표·종·성장도도 같다**.
  기본 유적과 이후 native 처리까지 포함한 완성 맵의 전역 동일성은 아래 실패 기록과 구분한다.
- 활성 효과는 실제 `GetDesiredPlantsCountAt`의 값이 native 공식 × 계획 가중치와 일치하는지 검사했다.
  생성 범위를 나가면 가중치1로 복원되며, 사막 fixture는 모든 가중치1/실제 식물208개를 관측했다.
  Native Scribe에서 null/none/natural 및 나머지 상태가 왕복 보존됐다.
- 같은 판정 함수에 실제 물→토양·영역 밖·legacy 층·식물 좌표/종 변조를 넣은 부정 대조7개가 검출됐다.
  단순한 임의 hash 문자열 비교를 검출기 검증으로 취급하지 않는다.

| 고정 native fixture | 자동 바닥 변경 칸 | 가중치가 적용된 칸 | 보호 확인 |
| --- | ---: | ---: | --- |
| 온대림 분지 + 명시 연못 | 347 | 11,788 | 연못·산·특수 지형 유지 |
| 강 타일 + 골짜기 | 13 | 22,343 | 얼음 아래 원래 물까지 관측 |
| 온천 + 분지 | 33 | 11,640 | 실제 HotSpring 표면 유지 |
| 사막 산기슭 | 3 | 0 | 사막의 native 밀도 규칙 |
| 70% 비옥토 + 길 | 0 | 0 | 전체 참조영역33,689칸·도로572칸 보호 |
| 산기슭 + 길 | 3 | 31,285 | 도로572칸 보호 |

가중치 칸 수는 실제로 새 식물을 생성한 칸 수가 아니다. 실제 식물/terrain 측정과 raw 실패,
실행별 DLL/probe 해시는 [native 보고서](native-report.md)와 [집계](native-summary.json)에 보존한다.
직접 실행 명령과 비교 도구는 [probe 안내](../../../tools/landscape-blend-probe/README.md)를 따른다.

## 발견한 문제와 보존한 실패

- 자체 검토: local road의 칠하지 않은 어깨가 Soil/Sand일 수 있어 s3부터 LocalRoadCells 전체와
  foundation/Road-tag 보호를 추가했다. 단순 terrain def만으로 길을 판단하지 않는다.
- 독립 검토: native AncientAsphaltRoad/Highway의 Gravel 어깨에는 Road tag가 없다.
  s3 이전 필드에서 `variant=23,x=0,z=10,waterDistance=5,rockDistance=20`이면 Gravel→Soil이
  가능했다. s4는 기존 Gravel 전체를 제외한다. 도로 출처를 추정하는 새 패치를 추가하지 않았다.
- 첫 native probe는 1MB가 넘는 식물 좌표를 제품 JSON serializer로 출력하다 크기 제한에 걸렸다.
  두 번째는 대체 serializer의 System.Web.Extensions가 native Mono에 없어 실패했다.
  둘 다 probe 실패이며 제품 성공 증거에서 제외한다. 원본 로그를 보존하고 probe 전용 streaming
  serializer로 수정했다. 이 실패 때문에 제품의 JSON 입력 제한을 완화하지 않았다.
- A의 off/on 비교에서 직접 blend 전후에는 보호 위반0이지만 나중에 생성된 바깥 유적216칸의
  바닥 차이가 발견됐다. 동일 제품/설정의 off/off 반복에서도 유적180칸과 식물 좌표가 달라졌다.
  이 조건의 native 전체 생성은 프로세스 간 동일하지 않으므로 이 차이를 새 기능만의 영향이나
  완전 보존의 증거로 사용하지 않는다. 비교 실패 JSON을 보존하고 legacy 비교 조건을 분리한다.
- B 첫 fixture는 강 타일에 mutator0을 요구해 타일을 찾지 못했다. 강 타일의 Lake category만
  제외하도록 probe 조건을 바로잡았다. 제품 기능 실패가 아닌 테스트 준비 실패로 기록한다.
- 후속 강 검사는 최상위 surface에 WaterMoving이 없다는 이유로 실패했지만, native ThinIce가
  임시층으로 덮은 것이었다. blend 직전/직후 및 Plants 이전 강은 유지됐다. 검출기를 underlying
  layer까지 읽도록 바꾸고 QuickTest 초기 RNG·TicksAbs도 고정해 비교했다. 초기 실패는 보존한다.
- 고정조건 r5의 legacy strict 비교도 기본 유적232칸과 최종 식물 차이를 보고했다. quiet r7에서
  지형·초기 식생의 동일성은 확인했지만 최종 식물은22,479/22,493으로 달라져 엄격 비교는 여전히
  실패다. 테스트의 적용 조건을 밝히며 모든 완성 맵/모든 생성 단계의 동일성으로 일반화하지 않는다.

## 한계와 다음 확인

이 단계는 물/산/식생의 주변 연결을 다듬는다. 자동 수계 설계, 물줄기와 계곡의 공동 재설계,
온천/유적 위치 자동 변경, 모든 mod 바이옴 생태계나 Geological Landforms 전체 목록은 포함하지 않는다.
원본 provider의 짧은 요청 해석과 추천 품질, 사용자 미관 만족도, 장시간 플레이는 별도 확인이 필요하다.
사막 또는 특수 밀도 곡선이 있는 맵에서 같은 식생 변화량을 약속하지 않는다.

2026-09-24에는 workspace 권한으로 원본 repo/게임/공유 트랙 기록을 쓸 수 없어 DEV 사본에 보관했다.
2026-09-26 사용자가 재개를 요청한 시점에는 권한이 열렸고 원본 dev/원격은 `1e33b8d`, clean,
게임은 종료 상태임을 다시 확인했다. 이전 사본의 커밋을 순서대로 이관하고 DEV만 백업/설치한다.
실제 완료와 설치·원격 해시는 mapgenai 공유 상태/최신 로그 및 `outputs/mapgenai-landscape-blending`
완료 영수증에 남긴다. 일반 배포와 이미지 입력 OFF를 유지하며 새 native 검증을 한 것으로 계산하지 않는다.
