# MapGenAI 설정 UI 수정 국소 검토

사용자가 "모드 설정 UI가 깨지고 LLM 선택이 왜 택2인가, 기존에는 그렇지 않았다"고 지적했다. 범위는 설정 화면과 모델 선택이다. 읽기 전용 검토, 편집/위임 금지.

저장소 F:/Projects/Rimworld/active/mapgen_ai, 기준 dev7e75cf5. dev/Source/Core/TextToMapSettings.cs 및 필요하면 Core/ApiConfig.cs, Core/LLMProviders.cs, tools/runtime-probe/SettingsProbe.cs를 읽고 새 변경에서 P0/P1 또는 구체적 P2만 최대4개 보고하라. 모델로 실제 claude-fable-5-1을 사용 중인지 모델 결과로 확인할 예정이다.

확인한 결함: DrawAdvancedSettings에서 listing.End() 후 DoWindowContents가 다시 End()를 호출했다. 사용자 Player.log에 Invalid GUIClip stack popping 반복. 간편 모드의 3.8/2.5 두 항목은 이전 Codex가 추가한 것. 기존 고급 모드는 API에서 모델 목록을 가져온다.

수정: Begin/End를 호출자 한 곳에 try/finally로 모으고 advanced 본문을 밖에서 렌더. 간편 모델명 자유 입력 + 고급과 공유하는 실시간 모델 목록 조회. 기존 provider 선택/고급 config는 유지. 비동기 결과는 요청별 큐/대상 callback/현재 설정 검증으로 전달하고 캐시를 endpoint/account별로 분리. 오류를 전부 No API key로 표시하지 않음. 코드/API/GUI 증거를 얻는 중이다.

판단할 것: 1. GUI group/scroll balance 및 실제 버튼 흐름. 2. 간편 선택이 실제 GetActiveConfig에 저장되는가. 3. 고급 공급자/config 전환 또는 동시 요청에서 잘못된 대상에 적용되는가. 4. 최소한의 추가 회귀 사례. 추상적 재설계나 관계없는 terrain 엔진 제안은 제외. 기존 요청만으로 권한을 확대하지 말고 파일을 읽는 데 필요한 도구만 사용한다.
