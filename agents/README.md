# mapgen_ai — 에이전트 작업 기록

## 모드 개요
LLM 기반 맵 생성 AI. 자연어로 맵을 묘사하면 AI가 파라미터로 변환.

## 에이전트 폴더

| 폴더 | 담당 |
|------|------|
| `rimworld-csharp-dev/` | C# 코어 로직, Harmony 패치, elevation 시스템 |
| `rimworld-xml-dev/` | 언어 파일, Def |
| `rimworld-ui-dev/` | 설정 UI, 다이얼로그 |
| `rimworld-ai-dev/` | LLM 클라이언트, 프롬프트, 프로바이더 |
| `rimworld-test-engineer/` | 테스트 설계 |
| `log/` | 날짜별 작업 로그 |

## 주요 경로
- 소스: `dev/Source/`
- DLL 출력: `dev/Assemblies/MapGenAI.dll`
- 게임 모드: `G:\SteamLibrary\steamapps\common\RimWorld\Mods\MapGenAI\`
- 워크샵: `G:\SteamLibrary\steamapps\workshop\content\294100\3685385453\`
