# TO_NEXT_SESSION.md

다음 세션에서 이 파일을 읽고 작업을 이어간다.

---

## 현재 상태: v1.5 (main에 merge 완료)

### 버전 히스토리
- v1.1: 워크샵 출시 버전
- v1.2: MDP WorldComponent + explicitKeys (LLM이 안 보낸 파라미터 유지)
- v1.3: CSG/SDF composite (별/하트/초승달 자유 형태)
- v1.4: terrain fill (모래/비옥한 토양/습지 등)
- v1.5: 프롬프트 50% 축소, 강 차단 코드화, 통로, UI 예시 업데이트

### GitHub
- main = v1.5
- feature/mdp-worldcomponent = main과 동일
- composite-v2-backup = CSG 백업
- 롤백: `git checkout v1.X && dotnet build`

---

## 다음 세션 할 일

### 1. 테스트 자동화 (우선순위 높음)
- `F:\Projects\Rimworld\test_automation\GUIDE.md` 참조
- test_automation 폴더는 공용 — 건드리지 말 것
- mapgen_ai 안에 테스트 파일 생성, test_automation의 core.py/runner.py를 import
- 좌표 실측 필요 (게임 켜서 MapGenAI 버튼, 입력창, 전송 버튼 좌표 기록)
- 시나리오: 기본 산/호수, MDP 유지, 강 방향, composite, terrain fill

### 2. 워크샵 업로드
- 인게임 테스트 목록 소화 후 업로드
- 업데이트 노트: "Added new terrain types and shape tools / Various improvements and bug fixes"
- 예시 프롬프트 업데이트됨 (4개 언어)

### 3. 알려진 이슈
- 통로: bump(negative)로 가능하지만 완벽하지 않음
- composite 8턴+: LLM이 shapes 축약하여 빠뜨릴 수 있음 (LLM 한계)
- 고양이 얼굴 같은 복잡한 형태: LLM 좌표 부정확
- 이미지→맵 변환: 시도했으나 색상 분류 부정확으로 폐기
- 하트 SDF: 어색함 (Quilez 공식 한계)

### 4. 미래 작업 후보
- function calling 전환 (프롬프트→function definition, 정확도 향상)
- 테스트 자동화 완성
- heightmap 재도전 (흑백 전용이면 가능할 수 있음)

---

## 이전 세션의 실수 — 반복하지 말 것

1. 에이전트 병렬 디스패치 후 검증 없이 완료 선언 금지
2. DLL은 반드시 로컬 Mods 폴더에 복사 (워크샵 폴더 X)
3. 유저가 요청한 것 먼저
4. 수정 전 근본 원인 파악
5. 프롬프트에 예외처리 넣지 말고 코드로 처리

---

## 파일 구조

```
dev/Source/
├── Core/           — TextToMapMod.cs, TextToMapSettings.cs, ApiConfig.cs, LLMProviders.cs
├── LLM/            — ILLMClient.cs, OpenAIClient.cs, GeminiClient.cs, LocalClient.cs
├── MapGen/         — MapGenParams.cs, TileMapState.cs, MapGenAIWorldComponent.cs,
│                     SdfComposite.cs, PresetManager.cs
├── Patches/        — GenStepPatches.cs, RiverPatches.cs, CoastPatches.cs, MountainSettingsPatch.cs,
│                     GeyserPatch.cs, OreDensityPatch.cs, BiomeDensityPatch.cs,
│                     RockTypesPatch.cs, RockChunkPatch.cs, RuinDangerDensityPatch.cs,
│                     TerrainFromPatch.cs, MapPreviewIntegration.cs, WorldInspectPane_Patch.cs
├── UI/             — Dialog_TextToMap.cs, SimpleJson.cs, L10n.cs
└── Tests/          — InGameTestRunner.cs, MdpApplyTests.cs, UnityShim.cs, VerseShim.cs

빌드: cd dev/Source && dotnet build
배포: cp dev/Assemblies/MapGenAI.dll "G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/"
```
