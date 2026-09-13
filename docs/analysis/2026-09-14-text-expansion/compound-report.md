# 연속 복합 요청 검증

2026-09-14. 기준 dev-text-baseline-2026-09-14 / f9f6aaa. 이미지 OFF, Fable/Claude 호출0회.

Gemini 3.8 Flash에 한국어8연속 요청을 실행했다. 물→용암과 폐허 크기/개수 동시 변경, 섬/해자 동시 이동, 자연 윤곽, 식은 용암과 개수 축소, 식생/동물 밀도, 참조 영역/폐허 동시 삭제, 없는 재료와 내륙 해안 제한 안내다. 매 턴 실제 현재 상태를 production BuildCurrentParamsText로 다시 전달했고 앞선 응답의 상태를 다음 요청에 이어 사용했다.

- [모델 결과](compound-r1/compound-results.json): 변경6·안내2, 의도/기존 상태 보존 모두 통과. 실패 응답을 재호출해 바꾸지 않았다.
- [실제 게임 결과](compound-native-r2/result.json): 완성맵6개 포함56검사 통과. 모델 원문8개를 실제 Dialog에 적용하고 Undo/안내의 무변경을 검사했다. 맵에서 실제 LavaDeep/CooledLava, 폐허 개수/벽 생성/전체 면적/이동된 섬 안 배치, 저장 상태와 월드 보존을 확인했다.
- 제품 DLL은 기준판 dbf21afce52d912137310c9a91c5914c794b50f208cf98d15f16b0a64883bb8f 그대로다. 이번 단계는 검증 도구와 결과를 추가했으며 제품 기능 수정은 없다.
- 첫 compound-native-r1은 probe에서 SimpleJson에 익명 객체를 넘겨 결과/관측 JSON이 빈 객체로 저장됐다. 필드 기반 serializer에 맞춰 Dictionary로 변경했다. 첫 결과는 최종 합격 근거에서 제외하고 원문을 보존했다. r2에서 결과/관측값이 실제로 기록됨을 확인했다.

재현:

```powershell
dotnet build tools/provider-probe/ProviderProbe.csproj --no-restore -v minimal
$env:MAPGENAI_PROBE_MODEL='gemini-3.8-flash'
dotnet run --project tools/provider-probe/ProviderProbe.csproj --no-build -- <repo> <새응답폴더> compound <기준native-verified폴더>
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore -v minimal
tools/runtime-probe/launch.ps1 -CompoundResponses <응답폴더> -Language Korean -Output <새실행폴더>
```

실제 프롬프트의 현재 상태 절을 교체하는 평가이며 전체 마우스 조작이나 모든 문장/모델의 정확성을 보장하지 않는다. 공간 관계·고대 위협 위치 확장은 이 검증과 분리해 구현/검증/커밋한다. 기존 태그와 설치판은 유지한다.
