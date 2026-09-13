# 지정 위치의 게임 기본 고대 위협

2026-09-14. 공간 관계 커밋49e4d54/09e25d1 다음 단계다. 기존 영역·전체 면적·충돌·지형 거리/방향·간격 검사를 공유하고 `ancient_danger` 생성기를 연결했다. Fable/Claude 호출0회, 이미지OFF 유지.

`AncientDangerGeneration`은 게임의 `ancientTemple` BaseGen 규칙을 실행한다. 로컬 게임 DLL의 `GenStep_ScatterShrines`, `SymbolResolver_AncientTemple`, `SymbolResolver_Interior_AncientTemple`, `BaseGen`을 확인했다. 디컴파일 원문은 scratch에서만 확인했으며 제품/저장소에 복사하지 않았다. 네이티브 구조물은 무작위 사원 외형, 지붕, 내부 시설, 동면관 내용물, 전리품, 적과 경고 장치를 게임 규칙에 따라 만든다. `peacefulTemples`는 비적대 내용물 규칙을 따른다.

원래 BaseGen 전역 설정과 symbolStack 참조를 보관하고 finally에서 복원한다. BaseGen 자체가 내부 예외를 처리하므로 호출 반환만 성공으로 보지 않고 실제 벽·바닥·지붕·전리품·경고 및 모든 새 건물/아이템/폰의 전체 면적을 검사한다. 모든 배치 공간 검사는 spawn 전에 끝내지만, 예상하지 못한 native 생성기 중간 실패에 대한 물체 전체 rollback까지 제공하지는 않는다.

범위는 가로·세로15~20칸, 계획별1~2개, 전체최대4개다. 위치/영역/경계/지형 관계/최소 간격을 공유하며 고대 위협의 회전은0만 허용한다. 특정 적/전리품 지정, 임의 모드 건물, 퀘스트 단지 생성은 지원하지 않는다. 단순 폐허의90도 회전 및 별도의 자연 생성 밀도 조절은 유지한다.

Map Preview의 배경 생성은 native 내부를 실행하지 않는다. **주황색 예약 테두리**만 그리며 실제 내부 내용물은 완성맵에서 생성한다. [미리보기 PNG](ancient-native-verified/ancient-preview.png)는250×250맵 중앙18×18예약선68칸이다. 시각 검토에서도 용암 고리 안 흙 섬의 주황 테두리를 확인했다.

검증 DLL: **4d02a121cd5b58cef52f9454c73650d622815dba8646b47a400d571db8a2e8df**.

- [오프라인100회귀](final-tests.log): 새 kind 저장/복사/밀도 분리, 크기/회전/개수 제한 및 원자적 거절 포함 PASS.
- [Gemini3.8 Flash 한국어5요청](ancient-provider-r1/ancient-results.json): 용암 섬 안18×18, 산 서쪽18×16/거리2~12, 회전 제한 안내, 퀘스트 단지 제한 안내, 자연 밀도만0.6으로 수정. 변경3·안내2, 의도/기존 상태 보존 모두 PASS. 재호출로 실패를 대체하지 않았다.
- [최종 native 결과](ancient-native-verified/result.json): **5완성맵+배경Map Preview56검사 PASS**. 실제 Dialog에 모델 원문을 적용하고 Undo/무변경을 대조했다. 섬/산기슭/두 개 간격/평화로운 난이도/공간 부족을 검사했다.
- [섬 관측값](ancient-native-verified/ancient-island-observation.json): 실제벽74, 지붕274, 바닥200, 동면관6/직접 내용물16, 전리품2, 적4, 경고2. 이 수치는 해당 seed의 관측값이며 사용자 요청의 보장 수량이 아니다. 실제 건물 존재·독립적인 컨테이너/지붕 수·전체 casket 면적을 다시 확인했다.
- [평화로운 맵](ancient-native-verified/ancient-peaceful-observation.json), [공간 부족](ancient-native-verified/ancient-capacity-observation.json), 실제 Scribe, BaseGen 참조 및 월드/계획 보존 검사도 포함한다.

초기 `ancient-native-r1`은65953966… DLL의41검사, `ancient-native-r2`는최종DLL의46검사다. 최종 verified56검사와 합산하지 않는다. r2에서 독립 내용물/지붕 대조와 밀도 요청 캡처를 추가했고 verified에서 모델5응답의 실제 적용/Undo10검사를 더했다.

재현:

```powershell
dotnet build dev/Source/MapGenAI.csproj --no-restore -v minimal
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore -v minimal
tools/runtime-probe/launch.ps1 -Ancient -Language Korean -Output <새캡처폴더>
dotnet build tools/provider-probe/ProviderProbe.csproj --no-restore -v minimal
$env:MAPGENAI_PROBE_MODEL='gemini-3.8-flash'
dotnet run --project tools/provider-probe/ProviderProbe.csproj --no-build -- <repo> <새응답폴더> ancient <캡처폴더>
tools/runtime-probe/launch.ps1 -Ancient -AncientResponses <응답폴더> -Language Korean -Output <새검증폴더>
```

실제 API 호출에는 로컬 개발 설정이 필요하며 키는 저장소/패키지에 포함하지 않는다. 재빌드 시 wildcard AssemblyVersion 때문에 DLL 해시가 달라질 수 있다. 이 결과는 RimWorld1.6.4871 rev591·설치된DLC·Harmony·Map Preview의 해당표본이며 모든 seed/모드/모델/오래된 사용자 세이브를 검증한 것은 아니다.
