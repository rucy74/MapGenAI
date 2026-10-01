# Map library developer prototype

GL 원본 생성 → 관측된 산/물 윤곽을 기존 MapGenAI 명령으로 변환 → 로컬 의미 검색 → 격리된 실제 RimWorld 맵 생성의 실험이다. **제품 채팅 UI·이미지 입력·설치 DLL은 변경하지 않는다.**

## 실제로 구현한 범위

- 설치된 Geological Landforms의 44 XML 그래프를 조사하고 6종을 원래 GL worker로 생성한다. GL은 일반 MapGenAI 숫자 설정이 아니라 생성 그래프다.
- 원본 그래프/seed/타일/라이선스는 재현 자료로 보관한다. 다른 타일에 쓰는 명령에는 seed·biome·mutator·전역 보너스를 이식하지 않는다.
- 실제 바위/수심 데이터에서 기존 polygon/sub/add 명령을 만든다. 작은 조각은 40칸 또는 면적 0.15% 미만이면 생략하고 손실을 기록한다. 일반 바닥은 현재 바이옴 생성기를 유지한다. 오아시스만 국소 Soil/SoilRich를 포함한다.
- 이미지 경로는 **알려진 Map Preview 팔레트의 물 윤곽/수심**만 읽는다. 갈색 토양/바위/그림자 혼동으로 산을 만들어 넣지 않는다. 일반 사진·UI가 덮인 스크린샷·다른 팔레트에 대한 범용 비전은 아니다.
- `multilingual-e5-small` 로컬 CPU 임베딩, 조건 필터, 가족별 중복 제거, 코사인 검색/다양성 조정. 신경망 reranker·유료 API·신규 LLM 제안은 사용하지 않는다.
- 원본과 다른 월드의 250/300 크기·온대림/건조관목림/사막을 실제로 생성해 비교한다. 지원조건 밖·기존 월드 강/해안/도로가 있는 타일·부분 편집·정확한 방향/비율 요청은 저장소 후보를 반환하지 않는다.
- 실제 신규 지도와 동일 타일/seed의 빈 설정 baseline을 비교한다. PNG는 native Map Preview 색으로 그린 실제 전체 맵이다. 얼음 표면은 PNG에 남기고 지형 측정은 임시 얼음 아래 영구 Water를 읽는다.

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
