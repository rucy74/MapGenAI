# 강·해안 타일의 온천 추가 — 2026-09-20

## 사용자 증상과 원인

사용자는 Odyssey 온천 특징이 존재하는데 남쪽 해안 타일에서 추가가 거부되고, 흙길이 강을 건너려다 실패하는 화면을 제공했다. 실제 Player.log의 MapGenAI 관련 응답만 [관찰 기록](user-observations.json)에 보존했다.

온천 거부는 모델의 기능 누락이 아니라 FeaturePolicy가 자연 발생 조건인 coastSidesRange=0 및 canSpawnOnRiver=false를 수동 추가에도 적용한 결과다. 이전에는 평지/바이옴 선호만 예외로 두었다. 모델에는 이 결과가 Unavailable로 전달되어 “게임 엔진상 불가능”이라고 과장해 설명했다.

도로 응답은 현재 지원 범위에 맞다. 도로는 물을 건너는 다리/터널을 생성하지 않는다. 로그의 360칸 장애물은 도로 요청 전부터 존재한 passage 경고이고, 이후 요청은 실제 DirtPath/avoid로 처리되었다. 이번 수정에서 강을 메우거나 도로 다리를 묵시적으로 추가하지 않는다.

## 변경

정확한 native TileMutatorWorker_HotSprings의 수동 추가만 강·해안 자연 발생 제한에서 제외했다. 다른 특징, 다른 온천 subclass, 강/해안 연결 삭제 금지, 다른 Lake 특징과의 충돌은 기존 규칙을 따른다.

native 온천은 기본적으로 River/Coast보다 먼저 GeneratePostTerrain을 실행한다. RiverTerrainAt은 이미 존재하는 비강 물을 건너뛰므로 온천수를 먼저 만들면 강이 끊길 수 있다. 따라서 현재 MapGenAI 생성 타일에서 명시한 native 온천에 한해, 강/해안이 있는 경우 PostTerrain 순서 복사본의 마지막으로 이동한다. Tile.Mutators 원본과 공유 def.genOrder, 다른 worker의 상대 순서, Init/고도 단계는 변경하지 않는다. 다른 경우에는 같은 원본 목록 객체를 반환한다.

온천 실행 전 기존 물 TerrainDef를 호출별로 보관하고 finalizer에서 복원한다. 강·바닷물·호숫가 물을 보존하면서 육지에 온천수와 암반을 만든다. Map Preview도 같은 경로를 쓴다. GenStep의 원래 foreach를 유지하고 getter 한 곳만 치환하며, 치환 개수가 1이 아니면 명시적으로 실패한다. 예외를 삼키지 않는다.

FeatureEditPrompt의 한국어/영어 설명도 동일하게 수정했다. Map Preview 모드 DLL/파일은 변경하지 않았다.

## 검증

기준 dev dba9b6faa39fa0cc3757b37e2181f4f91464ca5f, 복귀 태그 dev-before-coastal-hot-springs-2026-09-20. 최종 제품 DLL SHA256 47fa300bd24e0211559d4acdef338b2eee7a8897506bd48f0e7301f83cdb3ffb.

- 오프라인 회귀 174 PASS / 0 FAIL. native 수동 온천의 평지/강/해안 허용·월드 연결 보존과, 비대상 목록 객체/순서 유지·외부 subclass 제한을 검사했다.
- 이전 DLL은 같은 8개 중 내륙 1개만 생성했고 해안 2개/강 2개/강 하구 3개는 거부했다.
- 최종 8개 실제 맵과 8개 Map Preview에서 온천 생성과 물 지형 보존을 확인했다. 강/하구 5개는 native worker가 실제로 물을 덮으려는 148~521칸을 복원했다. 강의 연결 성분은 생성 전후 1개로 유지되었다. 월드 링크와 원본 mutator 순서도 같았다.
- Gemini 3.8 실제 요청 2개는 해안/강 하구 모두 HotSprings 특징 추가로 응답했다. 원문을 accepted-replies에 복사해 최종 DLL에서 재생했다. 나머지 6개는 고정 명령이며 모델 성공 횟수로 세지 않는다.
- 기존 지형 6개와 도로 14개의 완성 맵·Preview 40장, 기존 내륙 온천 완성 맵 1장이 이전 버전과 바이트 단위로 같았다.
- 최종 평가는 [evaluation.json](evaluation.json)에 기록했다. 양성 대조는 물 변경/월드 링크/원본 순서 판정기에 오염된 영수증을 넣어 거부를 확인했다. native 물 복원 자체는 실제 덮어쓰기 발생 사례로 확인했다. 내부 Codex 최종 읽기 전용 검토에서 차단급 결함은 발견하지 못했다.

## 자연 온천 대조에서 발견한 차이

원래 온천이 있는 산악 타일의 baseline/final 완성 맵은 온천수 695칸이 같지만 최종 PNG가 달랐다. 첫 비교 실패를 보존하고 native PostTerrain 종료 시 전체 지형 해시를 추가해 이전/최종 DLL을 다시 실행했다. 해당 단계의 지형 해시는 두 버전이 같았고 온천수도 695칸으로 같았다. **같은 이전 DLL을 재실행한 결과끼리도 최종 PNG가 달라졌다.** 따라서 이 사례의 전체 이미지 동일성은 합격 수치에서 제외하고, 온천 단계 동일성과 원래 버전에서도 발생하는 이후 생성 변동을 각각 기록했다. 뒤 단계 변동의 정확한 생성기 원인까지 규명한 것은 아니다.

초기 final-r1/natural-control은 preview 플래그가 없어 완성 맵만 생성했다. final-r2에서 8개의 Preview를 별도로 실행했으며 처음 실행을 Preview 성공으로 세지 않는다. native 자연 온천 대조는 완성 맵/단계 지형 검사이고 별도 Preview 대조는 미실행이다.

## 범위와 재현

고정 월드 seed와 격리 프로필 범위다. 사용자의 전체 모드 조합이나 당시 저장 맵 그대로를 재생한 것은 아니다. 온천 worker 예외를 인위적으로 주입해 finalizer 복원을 검사하는 별도 시험은 미실행이다. 기존 MG23 native 충돌은 이번에 해결했다고 주장하지 않는다. Fable 호출 0회, 이미지 입력 OFF 유지.

사용자가 “크레딧 많이 쓸 거 같으면 온천 작업까지만 해”라고 범위를 제한했다. 온천 수정·검증·저장·설치로 마무리하고 다리/도로 확장은 진행하지 않는다. 이 작업의 새 Gemini 호출은 2회이며 이후 추가 호출은 하지 않는다.

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --nologo
dotnet build tools/runtime-probe/RuntimeProbe.csproj --nologo
tools/runtime-probe/launch.ps1 -LandformSuite docs/analysis/2026-09-20-hot-springs-water/cases-final.json -LandformReplies docs/analysis/2026-09-20-hot-springs-water/accepted-replies -Language Korean -Output <새 절대 경로>
python -X utf8 tools/evaluate_hot_springs_water.py
```

새 게임에서 “온천을 지형 특징으로 추가해 줘”를 해안 또는 강 타일에 요청하고, 실제 온천수·강·해안을 확인할 수 있다. 기존 다른 호수 특징과 충돌하는 경우에는 여전히 교체 선택이 필요하다. [실제 그림](gallery.html).

native 근거는 설치된 RimWorld 1.6 Odyssey TileMutators_Natural.xml과 TileMutatorWorker_HotSprings, TileMutatorWorker_River, GenStep_MutatorPostTerrain의 로컬 어셈블리 코드다.
