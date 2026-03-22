# MapGen AI — 빠른 시작 가이드

## 사전 요구사항

- RimWorld 설치 (`G:\SteamLibrary\steamapps\common\RimWorld\`)
- Visual Studio 2022 또는 .NET SDK 4.7.2 (빌드용)
- Harmony 모드 (Steam Workshop ID: 2009463077)
- Map Preview 모드 (Steam Workshop ID: 2800857642)

---

## 개발 환경 설정

### 1. 프로젝트 열기

```
F:\Projects\Rimworld\mapgen_ai\dev\Source\MapGenAI.csproj
```

- Visual Studio 또는 Rider에서 열기
- 빌드 대상: `Debug` (개발 시) / `Release` (배포 시)

### 2. DLL 참조 경로 확인

`MapGenAI.csproj`의 HintPath가 실제 경로와 일치하는지 확인:

```xml
<!-- RimWorld 어셈블리 경로 -->
G:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll

<!-- Harmony -->
G:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll
```

경로가 다르면 `.csproj`에서 수정 필요.

### 3. 빌드

```
빌드 → 솔루션 빌드 (Ctrl+Shift+B)
```

출력: `dev/Assemblies/MapGenAI.dll`

빌드 성공 조건: 0 Error, 0 Warning (현재 기준)

---

## 게임 모드 적용

### 방법 A: 심볼릭 링크 (권장 — 빌드 즉시 반영)

```batch
mklink /D "G:\SteamLibrary\steamapps\common\RimWorld\Mods\MapGenAI" "F:\Projects\Rimworld\mapgen_ai\dev"
```

### 방법 B: 직접 복사

```batch
xcopy /E /I "F:\Projects\Rimworld\mapgen_ai\dev" "G:\SteamLibrary\steamapps\common\RimWorld\Mods\MapGenAI"
```

---

## API 키 설정 (인게임)

1. RimWorld 실행 → 메인 메뉴 → **모드 설정** → **MapGen AI**
2. **Simple 모드**: Gemini API 키만 입력 (무료 티어 사용 가능)
3. **Advanced 모드**: 프로바이더 선택 → API 키 입력 → 모델 선택

Gemini API 키 발급: https://aistudio.google.com/app/apikey

---

## 인게임 사용 방법

1. 월드맵에서 정착 타일 선택
2. 화면 좌측 하단 **"✦ AI Map Gen"** 버튼 클릭
3. 채팅창에 자유롭게 입력:
   - `"mountain fortress with hot springs"`
   - `"강이 왼쪽에, 평지, 나무 많이"`
   - `"just surprise me"`
4. AI 응답 확인 → **"이 설정으로 맵 생성"** 버튼 → 맵 시작

---

## 오프라인 API 테스트 (RimWorld 없이)

### 테스트 설정

```json
// docs/dev_config.json (git 제외, 직접 생성)
{
  "gemini_api_key": "YOUR_GEMINI_KEY",
  "openai_api_key": "YOUR_OPENAI_KEY"
}
```

### 테스트 실행

```
dev/Source/Tests/TextToMap.Tests.csproj  열기 (net8.0 또는 net10.0)
```

- 30개 LLM 응답 케이스 자동 검증
- 실행: `dotnet run` 또는 IDE에서 직접 실행
- 기대 결과: `30/30 PASS`

### 인게임 파이프라인 테스트

1. RimWorld 실행 → 월드맵에서 타일 선택
2. 개발 콘솔 열기 (좌측 상단 개발 모드 활성화 필요)
3. **Debug Actions → MapGenAI → Run All Pipeline Tests** 클릭
4. Player.log에서 `[MapGenAI]` 검색으로 13개 테스트 결과 확인

---

## 트러블슈팅

| 증상 | 원인 | 해결 |
|------|------|------|
| 빌드 오류: Assembly-CSharp 없음 | RimWorld DLL 경로 불일치 | `.csproj` HintPath 수정 |
| 번역 키가 그대로 출력 | `.Tr()` 메서드 사용 | `.Translate()` 로 교체 |
| AI 버튼이 보이지 않음 | 타일 미선택 | 월드맵에서 타일 클릭 후 재시도 |
| Mutator 적용 안 됨 | Odyssey DLC 비활성 | `ModsConfig.OdysseyActive` 확인 |
| LLM 응답 파싱 실패 | OpenRouter compact JSON | `OpenAIClient` 파싱 로직 확인 |

---

## 주요 로그 경로

```
%APPDATA%\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
```

`[MapGenAI]` 태그로 필터링하면 모든 파라미터 적용 과정 추적 가능.
