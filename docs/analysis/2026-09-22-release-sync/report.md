# MG30 — 일반 배포판 동기화

사용자 요청에 따라 검증된 DEV의 DLL과4개언어XML을 dist와 로컬 일반 MapGen AI에 동일하게 반영했다. C#기능/프롬프트/저장구조 변경은 없다. 기존모드는전체백업했고 `v1.6` 및 이전DEV태그는보존했다. Steam 업로드·새GitHubRelease·main병합은실행하지않았다.

- DLL source: `0b42e65ac3a98206933e3112c55382fdf9a52236` / SHA256 `17c35e94fe6c3a5277137a28ccfe3c6b12990dbc7679ca08ec4517e5736d8549`.
- 일반이름/ID: MapGen AI / Choco.MapGenAI. DEV와동시에활성화하지않도록상호제외메타데이터. 현재DLL에맞춰RimWorld1.6만표기.
- 9파일 ZIP/dist/설치본전부일치. DEV/일반판DLL+4번역도바이트일치. 기존Preview.png와워크숍PublishedFileId유지. ZIP에는PublishedFileId/소스/probe/API키/설정/세이브없음.
- 일반판만사용자활성목록에있음을확인했고모드목록·모든Config XML/API설정은변경하지않았다. 기존DEV8파일도불변. 예전 `Languages/Korean (한국어).bak`는전체백업후활성모드밖으로이동하여옛번역혼입을방지했다.
- 새검증: 회귀183 PASS, probe빌드0오류. 실제 `Mods/MapGenAI/Assemblies/MapGenAI.dll`을읽은별도프로필에서맵2+Preview2를생성. actualPackage=choco.mapgenai / DEV미로드를 product-load.json으로확인.
- 기본흙길·실제강의자동다리42칸·물/높이/월드연결보존·양안통행성공. Preview와완성맵전62500픽셀일치, MG29당시DEV의동일4그림과도일치. 총47검사 PASS. 새LLM/Fable호출0,이미지입력OFF.
- 임시observer probe자동정리확인. 초기probe의기존null-reference진단은남으므로Player.log무오류주장없음. 기존MG23 native충돌과전체사용자모드조합은이번동기화로해결됐다고주장하지않는다.

실행: `python tools/sync_release.py <verified-DEV-ZIP> <output>` → `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore` → `dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore` → 백업/설치 → `launch.ps1 -InstalledRelease -LandformSuite .../smoke-cases.json -LandformReplies .../2026-09-22-road-bridges/fixture-replies -Language Korean -Render -Output .../native-installed`.

[검증결과](evaluation.json) · [실제 로드](native-installed/product-load.json) · [일반판 맵](native-installed/suite-result.json) · [미리보기](native-installed/preview-result.json). 로컬배포ZIP/이전백업/설치영수증은 Codex outputs/mapgenai-release-sync에보관. ZIP SHA256 `81dfa2a382516c7b8fdebc39fb340349a508062621fb4ad01b267ceaf17ae6f9`.
