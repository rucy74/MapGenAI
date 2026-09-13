# MapGenAI 독립 코드 분석 요청

당신은 사용자가 명시적으로 지정한 Claude Fable 분석가입니다. 한국어로 답하세요.
파일 수정·추가 위임·API 호출·외부 탐색 없이 아래 제공된 원본 코드 발췌만 사용하세요. 도구를 쓰지 말고 한 응답으로 끝내세요.
기준: dev/v1.6, commit 1439bbdfac7601f6c668be49681f7d8f6073af09. 현재판 보존은 끝났고 이후 개선 방향을 정하는 분석 단계입니다.
사용자 핵심 가치: 기존 특징을 유지하면서 자연어로 맵을 계속 고치기. 과거 Opus로 이미지→맵과 복잡한 자연어 지형 둘 다 시도하다 포기했다고 밝혔습니다. 특정 원인이 당시 Opus 탓이라고 단정하지 마세요.

요청:
1. 이 모드가 실제로 하는 일과 할 수 없는 일(능력 경계).
2. 사용자에게 드러나는 결함/제약 최대 7개. 반드시 아래 파일/메서드와 구체적 입력→결과를 연결하고, 정적으로 확인한 사실과 런타임 가설을 구분.
3. 기존 엔진을 살리며 개선할 우선순위 5개 및 간단한 검증 방법.
4. 복잡한 지형 요청을 모델 성능만으로 해결할 수 있는지와 더 나은 표현/검증 방법.
5. 과잉 설계·성급한 새 모델 도입·현재 코드로 해결 불가능한 제안을 경계. 모델 의견끼리 일치해도 테스트 증거로 쓰지 않음.
아래 발췌의 범위 밖은 미확인이라고 쓰세요. 소스에 없다고 전체 저장소에 없다고 단정하지 마세요.


## dev/Source/UI/Dialog_TextToMap.cs L475-543
```csharp
475:         public Dialog_TextToMap()
476:         {
477:             doCloseButton = false;
478:             doCloseX = true;
479:             closeOnAccept = false;
480:             forcePause = false;  // Map Preview와 공존하기 위해 pause 안 함
481:             absorbInputAroundWindow = true;
482:             preventCameraMotion = false;
483:             layer = WindowLayer.Super;
484: 
485:             _openedTileId = Find.WorldSelector.SelectedTile;
486:             MapGenParams.CurrentTileId = _openedTileId;
487: 
488:             // WorldComponent에서 기존 타일 상태 로드
489:             MapGenParams.LoadFromTile(_openedTileId);
490:             _initialSnapshot = MapGenParams.HasParams ? MapGenParams.ToSnapshot() : null;
491: 
492:             // WorldComponent의 현재 상태 스냅샷 (닫기=취소 시 복원용)
493:             var wc = MapGenAI.MapGen.MapGenAIWorldComponent.Get();
494:             _wcSnapshot = wc?.GetState(_openedTileId)?.Clone();
495: 
496:             _history.Add(new ChatMessage("assistant",
497:                 "MapGenAI_Welcome".Translate()));
498: 
499:         }
500: 
501:         public override void DoWindowContents(Rect inRect)
502:         {
503:             // 백그라운드 스레드 응답 처리 (매 프레임 체크)
504:             if (_responseReady)
505:             {
506:                 _responseReady = false;
507:                 if (_pendingError != null)
508:                 {
509:                     // API 오류 (토큰 소진, 네트워크 등) → 채팅에 오류 표시
510:                     _history.Add(new ChatMessage("assistant", "MapGenAI_Error".Translate(_pendingError)));
511:                     _pendingError = null;
512:                     _isWaiting = false;
513:                     _statusText = "";
514:                 }
515:                 else
516:                 {
517:                     var resp = _pendingResponse;
518:                     _pendingResponse = null;
519:                     HandleResponse(resp);
520:                 }
521:             }
522: 
523:             var font = Text.Font;
524: 
525:             // 타이틀 바
526:             var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
527:             Widgets.DrawBoxSolid(titleRect, new Color(0.15f, 0.35f, 0.55f, 0.95f));
528:             Text.Font = GameFont.Small;
529:             var oldAnchor = Text.Anchor;
530:             Text.Anchor = TextAnchor.MiddleCenter;
531:             var oldColor = GUI.color;
532:             GUI.color = new Color(0.9f, 0.95f, 1f);
533:             Widgets.Label(titleRect, "MapGen AI");
534:             GUI.color = oldColor;
535:             Text.Anchor = oldAnchor;
536: 
537:             // 채팅 영역 (타이틀 아래)
538:             float topOffset = titleRect.yMax + 4f;
539:             float bottomReserve = InputHeight + 50f;
540:             var chatRect = new Rect(inRect.x, topOffset, inRect.width, inRect.height - topOffset - bottomReserve + inRect.y);
541:             DrawChat(chatRect);
542: 
543:             // 입력창 + 전송 버튼
```

## dev/Source/UI/Dialog_TextToMap.cs L693-857
```csharp
693:         private void SendMessage()
694:         {
695:             var text = _inputText.Trim();
696:             if (text == "" || _isWaiting) return;
697: 
698:             _inputText = "";
699:             _history.Add(new ChatMessage("user", text));
700:             _isWaiting = true;
701:             _statusText = "MapGenAI_Requesting".Translate();
702:             _paramsReady = false;
703: 
704:             var settings = MapGenAIMod.Settings;
705:             ILLMClient client;
706:             try
707:             {
708:                 var config = settings.GetActiveConfig();
709:                 if (config == null)
710:                 {
711:                     _statusText = "MapGenAI_Error".Translate("No API configured");
712:                     _isWaiting = false;
713:                     return;
714:                 }
715:                 client = LLMClientFactory.Create(config, settings.localBaseUrl);
716:             }
717:             catch (Exception e)
718:             {
719:                 Log.Error($"[MapGenAI] 클라이언트 생성 실패: {e}");
720:                 _statusText = "MapGenAI_Error".Translate(e.Message);
721:                 _isWaiting = false;
722:                 return;
723:             }
724: 
725:             Log.Message($"[MapGenAI] LLM 요청 시작");
726: 
727:             // 전송 전 현재 파라미터 스냅샷 저장 (undo용)
728:             if (MapGenParams.HasParams)
729:                 _paramStack.Push(MapGenParams.ToSnapshot());
730: 
731:             // LLM 컨텍스트: generate 후 초기화, ask 후 유지
732:             // 맵 상태는 system prompt에 MDP로 포함
733:             int tileId = Find.WorldSelector?.SelectedTile ?? -1;
734:             var systemPrompt = BuildSystemPrompt(tileId);
735: 
736:             _llmContext.Add(new ChatMessage("user", text));
737:             var historySnapshot = new List<ChatMessage>(_llmContext);
738: 
739:             Task.Run(async () =>
740:             {
741:                 string result = null;
742:                 string error = null;
743:                 try
744:                 {
745:                     Log.Message("[MapGenAI] Task.Run 시작");
746:                     result = await client.SendChatAsync(historySnapshot, systemPrompt);
747:                     Log.Message($"[MapGenAI] 응답 수신: {(result == null ? "null" : result.Length + "자")}");
748:                 }
749:                 catch (Exception e)
750:                 {
751:                     Log.Error($"[MapGenAI] API 오류: {e}");
752:                     // Fallback: 다음 유효한 config 시도
753:                     if (settings.TryNextConfig())
754:                     {
755:                         try
756:                         {
757:                             var nextClient = LLMClientFactory.Create(settings.GetActiveConfig(), settings.localBaseUrl);
758:                             if (nextClient != null)
759:                             {
760:                                 Log.Message("[MapGenAI] Fallback API 시도");
761:                                 result = await nextClient.SendChatAsync(historySnapshot, systemPrompt);
762:                             }
763:                             else error = e.Message;
764:                         }
765:                         catch (Exception e2)
766:                         {
767:                             Log.Error($"[MapGenAI] Fallback 오류: {e2}");
768:                             error = e2.Message;
769:                         }
770:                     }
771:                     else
772:                     {
773:                         error = e.Message;
774:                     }
775:                 }
776:                 _pendingResponse = result;
777:                 _pendingError = error;
778:                 _responseReady = true;
779:             });
780:         }
781: 
782:         private void HandleResponse(string response)
783:         {
784:             _isWaiting = false;
785:             if (response == null)
786:             {
787:                 _statusText = "MapGenAI_NoResponse".Translate();
788:                 return;
789:             }
790: 
791:             Log.Message($"[MapGenAI] HandleResponse 원문: {response}");
792: 
793:             try
794:             {
795:                 // JSON 추출: 첫 { ~ 마지막 } (코드블록/마크다운 무시)
796:                 int firstBrace = response.IndexOf('{');
797:                 int lastBrace = response.LastIndexOf('}');
798:                 if (firstBrace < 0 || lastBrace <= firstBrace)
799:                 {
800:                     _history.Add(new ChatMessage("assistant", response));
801:                     _statusText = "";
802:                     return;
803:                 }
804:                 var clean = response.Substring(firstBrace, lastBrace - firstBrace + 1);
805: 
806:                 var parsed = SimpleJson.Parse(clean);
807:                 var action = parsed.GetString("action");
808:                 Log.Message($"[MapGenAI] 파싱된 action: {action}");
809: 
810:                 if (action == "ask")
811:                 {
812:                     string askMsg = parsed.GetString("message");
813:                     _history.Add(new ChatMessage("assistant", askMsg));
814:                     _llmContext.Add(new ChatMessage("assistant", askMsg)); // ask는 컨텍스트 유지
815:                     _statusText = "";
816:                 }
817:                 else if (action == "generate")
818:                 {
819:                     _llmContext.Clear(); // 맵 변경 → 컨텍스트 초기화 (맵 상태는 system prompt에)
820:                     var data = ParseParams(parsed.GetObject("params"));
821: 
822:                     // --- Layer 3: 출력 검증 ---
823:                     var warnings = ValidateMutators(data);
824: 
825:                     // 강 없는 타일에서 river 파라미터 차단
826:                     ValidateRiver(data, warnings);
827: 
828:                     MapGenParams.Apply(data);
829:                     _paramsReady = true;
830:                     var desc = parsed.GetString("description") ?? "MapGenAI_ParamsSet".Translate().ToString();
831: 
832:                     // 경고 메시지가 있으면 채팅에 추가
833:                     string warningText = "";
834:                     if (warnings.Count > 0)
835:                     {
836:                         warningText = "\n\n" + string.Join("\n", warnings);
837:                         Log.Message($"[MapGenAI] 검증 경고 {warnings.Count}건: {string.Join("; ", warnings)}");
838:                     }
839: 
840:                     _history.Add(new ChatMessage("assistant",
841:                         $"{desc}{warningText}\n\n{"MapGenAI_ModifyHint".Translate()}"));
842:                     _statusText = "";
843:                 }
844:             }
845:             catch
846:             {
847:                 _history.Add(new ChatMessage("assistant",
848:                     IsKorean() ? "응답을 처리할 수 없습니다. 다시 시도해 주세요." : "Failed to process response. Please try again."));
849:                 _statusText = "";
850:             }
851:         }
852: 
853:         /// <summary>
854:         /// Layer 3: LLM 응답의 mutator 유효성 검증.
855:         /// 잘못된 mutator는 data에서 제거하고, 경고 메시지 목록을 반환.
856:         /// </summary>
857:         /// <summary>강 없는 타일에서 river 파라미터 차단.</summary>
```

## dev/Source/UI/Dialog_TextToMap.cs L966-1038
```csharp
966:         private MapParamsData ParseParams(SimpleJsonObject obj)
967:         {
968:             var data = new MapParamsData();
969: 
970:             // --- explicitKeys 추적: JSON에 키가 존재하면 기록 ---
971:             void Track(string key) { data.explicitKeys.Add(key); }
972: 
973:             if (obj.GetString("hills") != null)            { data.hills = obj.GetString("hills"); Track("hills"); }
974:             if (obj.GetString("hill_amount") != null)      { data.hill_amount = obj.GetFloat("hill_amount", 1f); Track("hill_amount"); }
975:             if (obj.GetString("vegetation_density") != null){ data.vegetation_density = obj.GetFloat("vegetation_density", 1f); Track("vegetation_density"); }
976:             if (obj.GetString("animal_density") != null)   { data.animal_density = obj.GetFloat("animal_density", 1f); Track("animal_density"); }
977:             if (obj.GetString("fertility_offset") != null) { data.fertility_offset = obj.GetFloat("fertility_offset", 0f); Track("fertility_offset"); }
978:             if (obj.GetString("roads") != null)            { data.roads = obj.GetBool("roads"); Track("roads"); }
979:             if (obj.GetString("caves") != null)            { data.caves = obj.GetBool("caves"); data.caves_explicit = true; Track("caves"); }
980:             if (obj.GetString("geysers") != null)          { data.geysers = obj.GetInt("geysers", -1); Track("geysers"); }
981:             if (obj.GetString("coast_direction") != null)  { data.coast_direction = obj.GetString("coast_direction"); Track("coast_direction"); }
982:             if (obj.GetString("rock_count") != null)       { data.rock_count = obj.GetInt("rock_count", -1); Track("rock_count"); }
983:             if (obj.GetString("ore_density") != null)      { data.ore_density = obj.GetFloat("ore_density", 1f); Track("ore_density"); }
984:             if (obj.GetString("ruin_density") != null)     { data.ruin_density = obj.GetFloat("ruin_density", 1f); Track("ruin_density"); }
985:             if (obj.GetString("danger_density") != null)   { data.danger_density = obj.GetFloat("danger_density", 1f); Track("danger_density"); }
986:             if (obj.GetString("rock_chunks") != null)      { data.rock_chunks = obj.GetBool("rock_chunks"); Track("rock_chunks"); }
987:             if (obj.GetString("hill_size") != null)        { data.hill_size = ParseHillSize(obj.GetString("hill_size")); Track("hill_size"); }
988:             if (obj.GetString("hill_smoothness") != null)  { data.hill_smoothness = ParseHillSmoothness(obj.GetString("hill_smoothness")); Track("hill_smoothness"); }
989:             if (obj.GetString("straight_river") != null)   { data.straight_river = obj.GetBool("straight_river"); Track("straight_river"); }
990: 
991:             // rock_types 배열 파싱
992:             var rockTypesArr = obj.GetArray("rock_types");
993:             if (rockTypesArr != null)
994:             {
995:                 data.rock_types = new System.Collections.Generic.List<string>();
996:                 foreach (var item in rockTypesArr)
997:                     data.rock_types.Add(item);
998:                 Track("rock_types");
999:             }
1000: 
1001:             // river 객체 파싱 — 세부 키별로 추적 (MDP: 방향만 보내도 위치 유지, 위치만 보내도 방향 유지)
1002:             var riverObj = obj.GetObject("river");
1003:             if (riverObj != null)
1004:             {
1005:                 data.river = new RiverData();
1006:                 if (riverObj.GetString("present") != null) { data.river.present = riverObj.GetBool("present"); Track("river_present"); }
1007:                 if (riverObj.GetString("direction") != null) { data.river.direction = riverObj.GetString("direction"); Track("river_direction"); }
1008:                 if (riverObj.GetString("direction_angle") != null) { data.river.direction_angle = riverObj.GetFloat("direction_angle", -1f); Track("river_direction"); }
1009:                 if (riverObj.GetString("x_position") != null) { data.river.x_position = riverObj.GetFloat("x_position", 0.5f); Track("river_position"); }
1010:                 if (riverObj.GetString("z_position") != null) { data.river.z_position = riverObj.GetFloat("z_position", 0.5f); Track("river_position"); }
1011:             }
1012: 
1013:             // river_direction / river_position 단축키 지원 (river 객체 없이 직접 지정 가능)
1014:             {
1015:                 string rdStr = obj.GetString("river_direction");
1016:                 string rpStr = obj.GetString("river_position");
1017:                 if (rdStr != null)
1018:                 {
1019:                     if (data.river == null) data.river = new RiverData();
1020:                     data.river.present = true;
1021:                     data.river.direction = rdStr;
1022:                     Track("river_direction");
1023:                     Track("river_present");
1024:                 }
1025:                 if (rpStr != null)
1026:                 {
1027:                     if (data.river == null) data.river = new RiverData();
1028:                     data.river.present = true;
1029:                     string rp = rpStr.Trim().ToLower();
1030:                     if (rp == "up" || rp == "top")
1031:                         data.river.z_position = 0.8f;
1032:                     else if (rp == "down" || rp == "bottom")
1033:                         data.river.z_position = 0.2f;
1034:                     else
1035:                         data.river.x_position = ParseRiverPosition(rpStr);
1036:                     Track("river_position");
1037:                     Track("river_present");
1038:                 }
```

## dev/Source/UI/Dialog_TextToMap.cs L1260-1404
```csharp
1260:         private void DoUndo()
1261:         {
1262:             if (_paramStack.Count == 0 || _isWaiting) return;
1263: 
1264:             var prev = _paramStack.Pop();
1265:             // Undo는 전체 적용 (explicitKeys 비어있음 → fullApply)
1266:             MapGenParams.Apply(prev, _openedTileId);
1267:             _paramsReady = true;
1268: 
1269:             // 마지막 user + assistant 메시지 쌍 제거 (환영 메시지는 유지)
1270:             if (_history.Count >= 3)
1271:                 _history.RemoveRange(_history.Count - 2, 2);
1272:             else if (_history.Count == 2)
1273:                 _history.RemoveAt(_history.Count - 1);
1274: 
1275:             _history.Add(new ChatMessage("assistant",
1276:                 IsKorean() ? "이전 상태로 되돌렸습니다." : "Reverted to previous state."));
1277:         }
1278: 
1279:         private void DoReset()
1280:         {
1281:             _paramStack.Clear();
1282: 
1283:             // WorldComponent도 대화 시작 시점으로 복원
1284:             var wc = MapGenAI.MapGen.MapGenAIWorldComponent.Get();
1285:             if (wc != null)
1286:             {
1287:                 if (_wcSnapshot != null)
1288:                     wc.SetState(_openedTileId, _wcSnapshot);
1289:                 else
1290:                     wc.RemoveState(_openedTileId);
1291:             }
1292: 
1293:             if (_initialSnapshot != null)
1294:             {
1295:                 MapGenParams.Apply(_initialSnapshot);
1296:                 _paramsReady = true;
1297:             }
1298:             else
1299:             {
1300:                 MapGenParams.Reset();
1301:                 _paramsReady = false;
1302:             }
1303: 
1304:             _history.Clear();
1305:             _history.Add(new ChatMessage("assistant", "MapGenAI_Welcome".Translate()));
1306:         }
1307: 
1308:         private void ShowPresetLoadMenu()
1309:         {
1310:             var presets = PresetManager.ListPresets();
1311:             if (presets.Count == 0)
1312:             {
1313:                 _history.Add(new ChatMessage("assistant", "MapGenAI_NoPresets".Translate()));
1314:                 return;
1315:             }
1316: 
1317:             var menuOptions = new List<FloatMenuOption>();
1318:             foreach (var name in presets)
1319:             {
1320:                 var presetName = name; // 클로저 캡처용 로컬 변수
1321:                 var option = new FloatMenuOption(presetName, () => LoadPreset(presetName));
1322:                 option.extraPartWidth = 30f;
1323:                 option.extraPartOnGUI = (Rect rect) =>
1324:                 {
1325:                     // X 삭제 버튼
1326:                     var xRect = new Rect(rect.x + 5f, rect.y + (rect.height - 20f) / 2f, 20f, 20f);
1327:                     var oldColor = GUI.color;
1328:                     bool xHover = xRect.Contains(Event.current.mousePosition);
1329:                     GUI.color = xHover ? new Color(1f, 0.3f, 0.3f) : new Color(0.6f, 0.3f, 0.3f);
1330:                     Text.Font = GameFont.Small;
1331:                     var oldAnchor = Text.Anchor;
1332:                     Text.Anchor = TextAnchor.MiddleCenter;
1333:                     Widgets.Label(xRect, "×");
1334:                     Text.Anchor = oldAnchor;
1335:                     GUI.color = oldColor;
1336:                     if (Widgets.ButtonInvisible(xRect))
1337:                     {
1338:                         PresetManager.Delete(presetName);
1339:                         _history.Add(new ChatMessage("assistant", "MapGenAI_PresetDeletedMsg".Translate(presetName)));
1340:                         return true; // 메뉴 닫기
1341:                     }
1342:                     return false;
1343:                 };
1344:                 menuOptions.Add(option);
1345:             }
1346: 
1347:             Find.WindowStack.Add(new FloatMenu(menuOptions));
1348:         }
1349: 
1350:         private void LoadPreset(string presetName)
1351:         {
1352:             var data = PresetManager.Load(presetName);
1353:             if (data == null)
1354:             {
1355:                 _history.Add(new ChatMessage("assistant", "MapGenAI_PresetLoadFailed".Translate(presetName)));
1356:                 return;
1357:             }
1358: 
1359:             MapGenParams.Apply(data);
1360:             _paramsReady = true;
1361:             _statusText = "";
1362:             _history.Add(new ChatMessage("assistant",
1363:                 "MapGenAI_PresetLoadedMsg".Translate(
1364:                     presetName, data.hills, data.hill_amount.ToString("F2"),
1365:                     data.vegetation_density.ToString("F1"), data.animal_density.ToString("F1"),
1366:                     (data.river?.present ?? false).ToString(), data.caves.ToString(),
1367:                     data.geysers.ToString())
1368:                 + "\n\n" + "MapGenAI_ModifyHint".Translate()));
1369:         }
1370: 
1371:         private void GenerateMap()
1372:         {
1373:             // "이 설정으로 맵 생성" 클릭 시: 파라미터 유지한 채로 닫기
1374:             _keepParams = true;
1375:             Close();
1376:             Messages.Message("MapGenAI_ParamsSaved".Translate(),
1377:                 MessageTypeDefOf.PositiveEvent);
1378:         }
1379: 
1380:         private bool _keepParams = false;
1381: 
1382:         public override void PostClose()
1383:         {
1384:             base.PostClose();
1385: 
1386:             if (!_keepParams)
1387:             {
1388:                 // 대화 취소/닫기 → WorldComponent를 대화 시작 시점으로 복원
1389:                 var wc = MapGenAI.MapGen.MapGenAIWorldComponent.Get();
1390:                 if (wc != null)
1391:                 {
1392:                     if (_wcSnapshot != null)
1393:                         wc.SetState(_openedTileId, _wcSnapshot);
1394:                     else
1395:                         wc.RemoveState(_openedTileId);
1396:                 }
1397: 
1398:                 MapGenParams.Reset();
1399:                 MapGenParams.RefreshMapPreview();
1400:                 Log.Message($"[MapGenAI] {"MapGenAI_DialogCancelled".Translate()}");
1401:             }
1402:         }
1403:     }
1404: 
```

## dev/Source/MapGen/MapGenParams.cs L1-57
```csharp
1: using System.Collections.Generic;
2: using System.Globalization;
3: using System.Linq;
4: using System.Runtime.CompilerServices;
5: using MapGenAI.LLM;
6: using RimWorld;
7: using RimWorld.Planet;
8: using UnityEngine;
9: using Verse;
10: 
11: namespace MapGenAI.MapGen
12: {
13:     /// <summary>
14:     /// Elevation 프리미티브 데이터 클래스.
15:     /// slope, radial, split, bump, noise 5종을 조합하여 지형 생성.
16:     /// 시맨틱("strong") 또는 숫자("0.8") 형태 모두 지원.
17:     /// </summary>
18:     public class ElevationShape : IExposable
19:     {
20:         public string type;         // slope, radial, split, bump, noise
21:         public string direction;    // left/right/top/bottom/top_left/top_right/bottom_left/bottom_right 또는 숫자(0-360)
22:         public string strength;     // weak/medium/strong/negative_weak/negative_medium/negative_strong 또는 숫자
23:         public string position;     // center/top_left/top/... 또는 숫자 배열 "0.5,0.5"
24:         public string size;         // small/medium/large 또는 숫자(0-1)
25:         public string gap;          // small/medium/large (split용)
26:         public string fill;         // null 또는 "water" (bump용, 호수 생성)
27:         public string fade;         // ridge용: small(0.3)/medium(0.5)/large(0.7) 또는 0~1
28:         public string noise_amount; // ridge용: none(0)/low(0.3)/medium(0.6)/high(1.0) 또는 0~1.5
29: 
30:         // composite (CSG/SDF) 전용 — type="composite"일 때 사용
31:         public List<ShapePrimitive> compositeShapes;
32:         public List<ComposeOp> compositeOps;
33: 
34:         public void ExposeData()
35:         {
36:             Scribe_Values.Look(ref type, "type");
37:             Scribe_Values.Look(ref direction, "direction");
38:             Scribe_Values.Look(ref strength, "strength");
39:             Scribe_Values.Look(ref position, "position");
40:             Scribe_Values.Look(ref size, "size");
41:             Scribe_Values.Look(ref gap, "gap");
42:             Scribe_Values.Look(ref fill, "fill");
43:             Scribe_Values.Look(ref fade, "fade");
44:             Scribe_Values.Look(ref noise_amount, "noise_amount");
45:         }
46: 
47:         public ElevationShape Clone()
48:         {
49:             return new ElevationShape
50:             {
51:                 type = type, direction = direction, strength = strength,
52:                 position = position, size = size, gap = gap, fill = fill,
53:                 fade = fade, noise_amount = noise_amount,
54:                 compositeShapes = compositeShapes,  // 참조 공유 OK (읽기 전용)
55:                 compositeOps = compositeOps
56:             };
57:         }
```

## dev/Source/MapGen/MapGenParams.cs L326-494
```csharp
326:         public static void Apply(MapParamsData data, int tileId)
327:         {
328:             // --- MDP 병합: WorldComponent에서 기존 타일 상태 로드, explicitKeys로 부분 업데이트 ---
329:             var wc = MapGenAIWorldComponent.Get();
330:             var existingState = wc?.GetState(tileId);
331:             TileMapState state = existingState?.Clone() ?? new TileMapState();
332:             var keys = data.explicitKeys;
333: 
334:             // explicitKeys가 비어있으면 (프리셋 로드, undo 등) 전체 적용 (기존 동작)
335:             bool fullApply = keys.Count == 0;
336: 
337:             // 스칼라 필드 병합: explicitKeys에 있으면 업데이트, 없으면 기존 state 유지
338:             if (fullApply || keys.Contains("hills"))
339:                 state.hills = ValidHills.Contains(data.hills ?? "") ? data.hills : "none";
340:             if (fullApply || keys.Contains("hill_amount"))
341:                 state.hillAmount = Mathf.Clamp(data.hill_amount, 0.1f, 1.6f);
342:             if (fullApply || keys.Contains("vegetation_density"))
343:                 state.vegetationDensity = Mathf.Clamp(data.vegetation_density, 0f, 2f);
344:             if (fullApply || keys.Contains("animal_density"))
345:                 state.animalDensity = Mathf.Clamp(data.animal_density, 0f, 2f);
346:             if (fullApply || keys.Contains("fertility_offset"))
347:                 state.fertilityOffset = Mathf.Clamp(data.fertility_offset, -1f, 1f);
348:             if (fullApply || keys.Contains("roads"))
349:                 state.hasRoads = data.roads;
350:             if (fullApply || keys.Contains("caves"))
351:             {
352:                 state.hasCaves = data.caves;
353:                 state.cavesExplicitlySet = data.caves_explicit;
354:             }
355:             if (fullApply || keys.Contains("geysers"))
356:                 state.geyserCount = (data.geysers >= 0) ? Mathf.Min(data.geysers, 20) : -1;
357:             if (fullApply || keys.Contains("coast_direction"))
358:             {
359:                 string coastDir = (data.coast_direction ?? "auto").ToLower();
360:                 state.coastDirection = ValidCoastDirections.Contains(coastDir) ? coastDir : "auto";
361:             }
362:             if (fullApply || keys.Contains("rock_count"))
363:                 state.rockCount = (data.rock_count >= 1) ? Mathf.Clamp(data.rock_count, 1, 15) : -1;
364:             if (fullApply || keys.Contains("ore_density"))
365:                 state.oreDensity = Mathf.Clamp(data.ore_density, 0f, 2.5f);
366:             if (fullApply || keys.Contains("ruin_density"))
367:                 state.ruinDensity = Mathf.Clamp(data.ruin_density, 0f, 2.5f);
368:             if (fullApply || keys.Contains("danger_density"))
369:                 state.dangerDensity = Mathf.Clamp(data.danger_density, 0f, 2.5f);
370:             if (fullApply || keys.Contains("rock_chunks"))
371:                 state.hasRockChunks = data.rock_chunks;
372:             if (fullApply || keys.Contains("hill_size"))
373:                 state.hillSize = data.hill_size > 0f ? Mathf.Clamp(data.hill_size, 0.005f, 0.1f) : 0.021f;
374:             if (fullApply || keys.Contains("hill_smoothness"))
375:                 state.hillSmoothness = data.hill_smoothness > 0f ? Mathf.Clamp(data.hill_smoothness, 0.5f, 6f) : 2.0f;
376:             if (fullApply || keys.Contains("straight_river"))
377:                 state.straightRiver = data.straight_river;
378: 
379:             // 강: river 키가 있으면 업데이트
380:             // 강: 세부 필드별로 병합 (방향만 보내도 위치 유지, 위치만 보내도 방향 유지)
381:             if (fullApply || keys.Contains("river_present"))
382:                 state.hasRiver = data.river?.present ?? false;
383:             if (fullApply || keys.Contains("river_direction"))
384:             {
385:                 float angle = data.river?.direction_angle ?? -1f;
386:                 // 하위 호환: direction 문자열 → angle 변환
387:                 if (angle < 0f && data.river != null)
388:                 {
389:                     string dir = data.river.direction?.ToLower();
390:                     if (dir == "horizontal") angle = 90f;
391:                     else if (dir == "vertical") angle = -1f;
392:                     else if (dir == "left") angle = 270f;
393:                     else if (dir == "up") angle = 0f;
394:                     else if (dir == "right") angle = 90f;
395:                     else if (dir == "down") angle = 180f;
396:                     else if (float.TryParse(dir, System.Globalization.NumberStyles.Float,
397:                         System.Globalization.CultureInfo.InvariantCulture, out float parsed))
398:                         angle = Mathf.Repeat(parsed, 360f);
399:                 }
400:                 else if (angle >= 0f)
401:                     angle = Mathf.Repeat(angle, 360f);
402:                 state.riverDirectionAngle = angle;
403:             }
404:             if (fullApply || keys.Contains("river_position"))
405:             {
406:                 state.riverXPosition = Mathf.Clamp(data.river?.x_position ?? 0.5f, 0f, 1f);
407:                 state.riverZPosition = Mathf.Clamp(data.river?.z_position ?? 0.5f, 0f, 1f);
408:             }
409: 
410:             // 석재 종류
411:             if (fullApply || keys.Contains("rock_types"))
412:             {
413:                 state.rockTypes.Clear();
414:                 if (data.rock_types != null)
415:                     foreach (var rt in data.rock_types)
416:                         if (!string.IsNullOrEmpty(rt)) state.rockTypes.Add(rt);
417:             }
418: 
419:             // TileMutator — additive 병합.
420:             // "추가"는 기존 특징에 더하기(union)로, "제거"는 remove_mutators로만.
421:             // 구버전은 mutators 키가 오면 clear+reset(full-replace)이라, "오로라 추가"처럼
422:             // 새 것 하나만 보내면 기존 별관측 등이 지워지는 버그가 있었음 → union으로 교체.
423:             // fullApply(프리셋/undo)는 완전한 스냅샷이므로 clear 후 대입(교체 의미 유지).
424:             if (fullApply || keys.Contains("mutators"))
425:             {
426:                 if (fullApply) state.mutators.Clear();
427:                 if (data.mutators != null)
428:                     foreach (var m in data.mutators)
429:                         if (!string.IsNullOrEmpty(m) && !state.mutators.Contains(m))
430:                             state.mutators.Add(m);
431:             }
432:             if (fullApply || keys.Contains("remove_mutators"))
433:             {
434:                 state.removeMutators.Clear();
435:                 if (data.remove_mutators != null)
436:                     foreach (var m in data.remove_mutators)
437:                         if (!string.IsNullOrEmpty(m))
438:                         {
439:                             state.removeMutators.Add(m);
440:                             state.mutators.Remove(m);  // desired-set에서도 빼야 재적용 때 안 살아남
441:                         }
442:             }
443: 
444:             // ElevationShapes: 키가 있으면 전체 교체, 없으면 기존 유지
445:             if (fullApply || keys.Contains("elevation_shapes"))
446:             {
447:                 state.elevationShapes.Clear();
448:                 if (data.elevation_shapes != null)
449:                     foreach (var shape in data.elevation_shapes)
450:                         if (shape != null && !string.IsNullOrEmpty(shape.type))
451:                             state.elevationShapes.Add(shape.Clone());
452:             }
453: 
454:             // hills shape 누적: 같은 type+direction이 없으면 추가
455:             if (state.hills != "none")
456:             {
457:                 var autoShape = GetAutoShapeForHills(state.hills);
458:                 if (autoShape != null)
459:                 {
460:                     bool exists = state.elevationShapes.Any(s =>
461:                         s.type == autoShape.type &&
462:                         (s.direction ?? "") == (autoShape.direction ?? "") &&
463:                         (s.position ?? "") == (autoShape.position ?? "") &&
464:                         string.IsNullOrEmpty(s.fill));
465:                     if (!exists)
466:                         state.elevationShapes.Add(autoShape);
467:                 }
468:             }
469: 
470:             // --- WorldComponent에 상태 저장 ---
471:             if (wc != null && tileId >= 0)
472:                 wc.SetState(tileId, state);
473: 
474:             // --- 정적 필드 업데이트 (패치용 캐시) ---
475:             ApplyStateToStaticFields(state);
476: 
477:             HasParams = true;
478: 
479:             Verse.Log.Message($"[MapGenAI] 파라미터 적용 (tile={tileId}, explicit={keys.Count}): " +
480:                 $"언덕={Hills}, 산양={HillAmount:F2}, 나무={VegetationDensity:F1}, " +
481:                 $"동물={AnimalDensity:F1}, 강={HasRiver}(방향={RiverDirectionAngle:F0}, X={RiverXPosition:F2}, Z={RiverZPosition:F2}), " +
482:                 $"동굴={HasCaves}, 간헐천={GeyserCount}, 해안={CoastDirection}, " +
483:                 $"석재수={RockCount}, 석재종류={RockTypes.Count}개, 광석밀도={OreDensity:F2}, " +
484:                 $"폐허밀도={RuinDensity:F2}, 위험밀도={DangerDensity:F2}, " +
485:                 $"돌덩어리={HasRockChunks}, 산크기={HillSize:F4}, 산부드러움={HillSmoothness:F1}, " +
486:                 $"mutators={Mutators.Count}개, elevation_shapes={ElevationShapes.Count}개");
487: 
488:             // 월드 타일에 mutator 영구 적용 (Map Designer 방식)
489:             ApplyMutatorsToWorldTile();
490: 
491:             RefreshMapPreview();
492:         }
493: 
494:         /// <summary>TileMapState를 정적 필드에 적용 (패치들이 읽는 캐시).</summary>
```

## dev/Source/MapGen/MapGenParams.cs L563-746
```csharp
563:         private static void ApplyMutatorsToWorldTile()
564:         {
565:             try
566:             {
567:                 var tileId = Verse.Find.WorldSelector?.SelectedTile ?? -1;
568:                 if (tileId < 0) return;
569:                 var tile = Verse.Find.WorldGrid?[tileId];
570:                 if (tile == null) return;
571: 
572:                 // 이전 적용이 있으면 먼저 복원 (Undo 시에도 이전 mutator가 제거되어야 함)
573:                 bool hadPreviousApplication = _mutatorAppliedTileId >= 0;
574:                 if (hadPreviousApplication)
575:                     RestoreMutatorsFromWorldTile();
576: 
577:                 bool hasMutatorChanges = Mutators.Count > 0;
578:                 bool hasRemoveMutators = RemoveMutators.Count > 0;
579:                 // 현재 타일에 Caves mutator가 있는지 (복원 후 기준)
580:                 bool tileHasCaves = tile.Mutators.Any(m => m.defName == "Caves");
581:                 // caves 추가가 필요한지 (HasCaves=true이고 타일에 없을 때만)
582:                 bool needCavesAdd = HasCaves && !tileHasCaves;
583:                 // caves 제거: LLM이 명시적으로 caves=false 보냈거나 remove_mutators에 "Caves" 있을 때
584:                 bool needCavesRemove = tileHasCaves &&
585:                     (RemoveMutators.Contains("Caves") || (CavesExplicitlySet && !HasCaves));
586: 
587:                 // 원본 저장 (복원 후 다시 읽기) — hilliness도 변경할 수 있으므로 항상 저장
588:                 _mutatorAppliedTileId = tileId;
589:                 _originalMutatorDefNames = tile.Mutators.Select(m => m.defName).ToList();
590: 
591:                 // 1. Caves mutator 추가/제거
592:                 if (needCavesAdd || needCavesRemove)
593:                 {
594:                     var cavesMutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail("Caves");
595:                     if (cavesMutDef != null)
596:                     {
597:                         if (needCavesAdd)
598:                         {
599:                             tile.AddMutator(cavesMutDef);
600:                             Verse.Log.Message("[MapGenAI] 동굴 mutator 추가");
601:                         }
602:                         else if (needCavesRemove)
603:                         {
604:                             tile.RemoveMutator(cavesMutDef);
605:                             Verse.Log.Message("[MapGenAI] 동굴 mutator 제거");
606:                         }
607:                     }
608:                 }
609: 
610:                 // 2. LLM이 지정한 mutator 추가 (기존 유지, 같은 카테고리만 교체)
611:                 // "교체 모드" 제거 — 기존 River/Coast/Mountain은 유지됨
612:                 foreach (var defName in Mutators)
613:                 {
614:                     var mutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
615:                     if (mutDef == null) continue;
616: 
617:                     // River 카테고리는 강이 있어야 함
618:                     if (mutDef.categories.Contains("River"))
619:                     {
620:                         var st = tile as RimWorld.Planet.SurfaceTile;
621:                         if (st?.Rivers == null || st.Rivers.Count == 0)
622:                         {
623:                             Verse.Log.Message($"[MapGenAI] '{defName}' 스킵 — 강이 없는 타일");
624:                             continue;
625:                         }
626:                     }
627: 
628:                     // Coast 카테고리는 해안이어야 함
629:                     if (mutDef.categories.Contains("Coast"))
630:                     {
631:                         if (!tile.Mutators.Any(m => m.defName == "Coast") && !tile.IsCoastal)
632:                         {
633:                             Verse.Log.Message($"[MapGenAI] '{defName}' 스킵 — 해안이 아닌 타일");
634:                             continue;
635:                         }
636:                     }
637: 
638:                     // 같은 카테고리 기존 mutator만 교체 (다른 카테고리는 유지)
639:                     var toRemove = tile.Mutators
640:                         .Where(m => m.categories.Any(c => mutDef.categories.Contains(c)))
641:                         .ToList();
642:                     foreach (var old in toRemove)
643:                         tile.RemoveMutator(old);
644: 
645:                     tile.AddMutator(mutDef);
646:                     Verse.Log.Message($"[MapGenAI] 타일 mutator 적용: {mutDef.label} ({defName})");
647:                 }
648: 
649:                 // 3. 제거할 mutator 처리 (동굴 제거, 동물 개체수 감소 제거 등)
650:                 foreach (var defName in RemoveMutators)
651:                 {
652:                     var mutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
653:                     if (mutDef == null) continue;
654:                     if (tile.Mutators.Contains(mutDef))
655:                     {
656:                         tile.RemoveMutator(mutDef);
657:                         Verse.Log.Message($"[MapGenAI] 타일 mutator 제거: {mutDef.label} ({defName})");
658:                     }
659:                 }
660: 
661:                 // hilliness 변경 제거됨 — Mountainous로 바꾸면 맵 전체가 바위로 뒤덮이는 문제.
662:                 // shapes 누적으로 양쪽 산 문제는 해결됨.
663:             }
664:             catch (System.Exception e)
665:             {
666:                 Verse.Log.Warning($"[MapGenAI] Mutator 적용 실패: {e.Message}");
667:             }
668:         }
669: 
670:         /// <summary>
671:         /// hill_amount 기반으로 타일의 hilliness를 변경.
672:         /// 바닐라 factor: Flat=0.8, SmallHills=0.9, LargeHills=1.0, Mountainous=1.1, Impassable=1.2
673:         /// hill_amount가 높으면 hilliness를 올려서 바닐라 산 생성 로직이 자연스러운 산을 만들도록 함.
674:         /// </summary>
675:         private static void ApplyHillinessToWorldTile(Tile tile)
676:         {
677:             // hill_amount → hilliness 매핑
678:             Hilliness target;
679:             if (HillAmount <= 0.5f)
680:                 target = Hilliness.Flat;
681:             else if (HillAmount <= 0.85f)
682:                 target = Hilliness.SmallHills;
683:             else if (HillAmount <= 1.05f)
684:                 target = Hilliness.LargeHills;
685:             else if (HillAmount <= 1.3f)
686:                 target = Hilliness.Mountainous;
687:             else
688:                 target = Hilliness.Impassable;
689: 
690:             // elevation_shapes에 산 관련 shape이 있으면 최소 Mountainous 보장
691:             // Mountainous = factor 1.1 + DistFromAxis 산맥 추가.
692:             // LargeHills(1.0)로는 base 산이 안 생김.
693:             if (ElevationShapes.Any(s => s.type == "ridge" || s.type == "radial" || s.type == "ring"))
694:             {
695:                 if (target < Hilliness.Mountainous)
696:                     target = Hilliness.Mountainous;
697:             }
698: 
699:             if (tile.hilliness == target) return;
700: 
701:             // 원본 저장 (아직 안 했으면)
702:             if (!_hillinessModified)
703:             {
704:                 _originalHilliness = tile.hilliness;
705:                 _hillinessModified = true;
706:             }
707: 
708:             tile.hilliness = target;
709:             Verse.Log.Message($"[MapGenAI] 타일 hilliness 변경: {_originalHilliness} → {target}");
710:         }
711: 
712:         /// <summary>원본 mutator + hilliness 복원</summary>
713:         private static void RestoreMutatorsFromWorldTile()
714:         {
715:             if (_mutatorAppliedTileId < 0 || _originalMutatorDefNames == null) return;
716: 
717:             try
718:             {
719:                 var tile = Verse.Find.WorldGrid?[_mutatorAppliedTileId];
720:                 if (tile == null) return;
721: 
722:                 // hilliness 복원
723:                 if (_hillinessModified)
724:                 {
725:                     tile.hilliness = _originalHilliness;
726:                     _hillinessModified = false;
727:                 }
728: 
729:                 // 현재 mutator 전부 제거
730:                 foreach (var m in tile.Mutators.ToList())
731:                     tile.RemoveMutator(m);
732: 
733:                 // 원본 복원
734:                 foreach (var defName in _originalMutatorDefNames)
735:                 {
736:                     var mutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
737:                     if (mutDef != null)
738:                         tile.AddMutator(mutDef);
739:                 }
740:             }
741:             catch { }
742: 
743:             _mutatorAppliedTileId = -1;
744:             _originalMutatorDefNames = null;
745:         }
746: 
```

## dev/Source/MapGen/MapGenParams.cs L798-839
```csharp
798:         public static MapParamsData ToSnapshot()
799:         {
800:             return new MapParamsData
801:             {
802:                 hills = Hills,
803:                 hill_amount = HillAmount,
804:                 vegetation_density = VegetationDensity,
805:                 animal_density = AnimalDensity,
806:                 fertility_offset = FertilityOffset,
807:                 river = new RiverData
808:                 {
809:                     present = HasRiver,
810:                     direction_angle = RiverDirectionAngle,
811:                     x_position = RiverXPosition,
812:                     z_position = RiverZPosition
813:                 },
814:                 roads = HasRoads,
815:                 caves = HasCaves,
816:                 caves_explicit = CavesExplicitlySet,
817:                 geysers = GeyserCount,
818:                 coast_direction = CoastDirection,
819:                 rock_count = RockCount,
820:                 ore_density = OreDensity,
821:                 ruin_density = RuinDensity,
822:                 danger_density = DangerDensity,
823:                 rock_chunks = HasRockChunks,
824:                 hill_size = HillSize,
825:                 hill_smoothness = HillSmoothness,
826:                 straight_river = StraightRiver,
827:                 rock_types = new List<string>(RockTypes),
828:                 mutators = new List<string>(Mutators),
829:                 remove_mutators = new List<string>(RemoveMutators),
830:                 elevation_shapes = ElevationShapes.Select(s => new ElevationShape
831:                 {
832:                     type = s.type, direction = s.direction, strength = s.strength,
833:                     position = s.position, size = s.size, gap = s.gap, fill = s.fill,
834:                     fade = s.fade, noise_amount = s.noise_amount
835:                 }).ToList()
836:             };
837:         }
838: 
839:         /// <summary>현재 파라미터 상태를 시스템 프롬프트용 텍스트로 반환.</summary>
```

## dev/Source/MapGen/TileMapState.cs L1-130
```csharp
1: using System.Collections.Generic;
2: using System.Linq;
3: using Verse;
4: 
5: namespace MapGenAI.MapGen
6: {
7:     /// <summary>
8:     /// 타일의 현재 맵 생성 상태. WorldComponent에 영구 저장됨.
9:     /// 이건 "이전 생성 이력"이 아니라 "타일이 현재 어떤 상태인지"를 나타냄.
10:     /// 바닐라 tile.hilliness와 동일한 역할 — 방향/위치 등 세밀한 정보 포함.
11:     /// </summary>
12:     public class TileMapState : IExposable
13:     {
14:         // 지형
15:         public string hills = "none";
16:         public float hillAmount = 1f;
17:         public List<ElevationShape> elevationShapes = new List<ElevationShape>();
18:         public float vegetationDensity = 1f;
19:         public float fertilityOffset = 0f;
20:         public float animalDensity = 1f;
21: 
22:         // 수계
23:         public bool hasRiver = false;
24:         public float riverDirectionAngle = -1f;
25:         public float riverXPosition = 0.5f;
26:         public float riverZPosition = 0.5f;
27:         public bool straightRiver = false;
28: 
29:         // 지물
30:         public bool hasRoads = false;
31:         public bool hasCaves = false;
32:         public bool cavesExplicitlySet = false;
33:         public int geyserCount = -1;
34:         public bool hasRockChunks = true;
35:         public float hillSize = 0.021f;
36:         public float hillSmoothness = 2.0f;
37: 
38:         // TileMutator
39:         public List<string> mutators = new List<string>();
40:         public List<string> removeMutators = new List<string>();
41: 
42:         // 해안/석재/광석/폐허
43:         public string coastDirection = "auto";
44:         public int rockCount = -1;
45:         public float oreDensity = 1f;
46:         public List<string> rockTypes = new List<string>();
47:         public float ruinDensity = 1f;
48:         public float dangerDensity = 1f;
49: 
50:         public void ExposeData()
51:         {
52:             Scribe_Values.Look(ref hills, "hills", "none");
53:             Scribe_Values.Look(ref hillAmount, "hillAmount", 1f);
54:             Scribe_Values.Look(ref vegetationDensity, "vegetationDensity", 1f);
55:             Scribe_Values.Look(ref fertilityOffset, "fertilityOffset", 0f);
56:             Scribe_Values.Look(ref animalDensity, "animalDensity", 1f);
57: 
58:             Scribe_Values.Look(ref hasRiver, "hasRiver", false);
59:             Scribe_Values.Look(ref riverDirectionAngle, "riverDirectionAngle", -1f);
60:             Scribe_Values.Look(ref riverXPosition, "riverXPosition", 0.5f);
61:             Scribe_Values.Look(ref riverZPosition, "riverZPosition", 0.5f);
62:             Scribe_Values.Look(ref straightRiver, "straightRiver", false);
63: 
64:             Scribe_Values.Look(ref hasRoads, "hasRoads", false);
65:             Scribe_Values.Look(ref hasCaves, "hasCaves", false);
66:             Scribe_Values.Look(ref cavesExplicitlySet, "cavesExplicitlySet", false);
67:             Scribe_Values.Look(ref geyserCount, "geyserCount", -1);
68:             Scribe_Values.Look(ref hasRockChunks, "hasRockChunks", true);
69:             Scribe_Values.Look(ref hillSize, "hillSize", 0.021f);
70:             Scribe_Values.Look(ref hillSmoothness, "hillSmoothness", 2.0f);
71: 
72:             Scribe_Values.Look(ref coastDirection, "coastDirection", "auto");
73:             Scribe_Values.Look(ref rockCount, "rockCount", -1);
74:             Scribe_Values.Look(ref oreDensity, "oreDensity", 1f);
75:             Scribe_Values.Look(ref ruinDensity, "ruinDensity", 1f);
76:             Scribe_Values.Look(ref dangerDensity, "dangerDensity", 1f);
77: 
78:             Scribe_Collections.Look(ref elevationShapes, "elevationShapes", LookMode.Deep);
79:             Scribe_Collections.Look(ref mutators, "mutators", LookMode.Value);
80:             Scribe_Collections.Look(ref removeMutators, "removeMutators", LookMode.Value);
81:             Scribe_Collections.Look(ref rockTypes, "rockTypes", LookMode.Value);
82: 
83:             // 로드 시 null 방지
84:             if (Scribe.mode == LoadSaveMode.PostLoadInit)
85:             {
86:                 if (elevationShapes == null) elevationShapes = new List<ElevationShape>();
87:                 if (mutators == null) mutators = new List<string>();
88:                 if (removeMutators == null) removeMutators = new List<string>();
89:                 if (rockTypes == null) rockTypes = new List<string>();
90:             }
91:         }
92: 
93:         /// <summary>깊은 복사 (undo 스냅샷용).</summary>
94:         public TileMapState Clone()
95:         {
96:             return new TileMapState
97:             {
98:                 hills = hills,
99:                 hillAmount = hillAmount,
100:                 vegetationDensity = vegetationDensity,
101:                 fertilityOffset = fertilityOffset,
102:                 animalDensity = animalDensity,
103:                 hasRiver = hasRiver,
104:                 riverDirectionAngle = riverDirectionAngle,
105:                 riverXPosition = riverXPosition,
106:                 riverZPosition = riverZPosition,
107:                 straightRiver = straightRiver,
108:                 hasRoads = hasRoads,
109:                 hasCaves = hasCaves,
110:                 cavesExplicitlySet = cavesExplicitlySet,
111:                 geyserCount = geyserCount,
112:                 hasRockChunks = hasRockChunks,
113:                 hillSize = hillSize,
114:                 hillSmoothness = hillSmoothness,
115:                 mutators = new List<string>(mutators),
116:                 removeMutators = new List<string>(removeMutators),
117:                 coastDirection = coastDirection,
118:                 rockCount = rockCount,
119:                 oreDensity = oreDensity,
120:                 rockTypes = new List<string>(rockTypes),
121:                 ruinDensity = ruinDensity,
122:                 dangerDensity = dangerDensity,
123:                 elevationShapes = elevationShapes.Select(s => s.Clone()).ToList()
124:             };
125:         }
126: 
127:         /// <summary>기본값(빈 상태)인지 확인.</summary>
128:         public bool IsDefault()
129:         {
130:             return hills == "none" && hillAmount == 1f && elevationShapes.Count == 0
```

## dev/Source/UI/SimpleJson.cs L1-180
```csharp
1: using System.Collections.Generic;
2: using System.Globalization;
3: 
4: namespace MapGenAI.UI
5: {
6:     /// <summary>
7:     /// 외부 라이브러리 없이 쓰는 최소 JSON 파서 (LLM 응답 파싱용)
8:     /// </summary>
9:     public class SimpleJsonObject
10:     {
11:         private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
12:         private readonly Dictionary<string, SimpleJsonObject> _objects = new Dictionary<string, SimpleJsonObject>();
13:         private readonly Dictionary<string, List<string>> _arrays = new Dictionary<string, List<string>>();
14:         private readonly Dictionary<string, List<SimpleJsonObject>> _objectArrays = new Dictionary<string, List<SimpleJsonObject>>();
15:         private readonly Dictionary<string, List<List<string>>> _nestedArrays = new Dictionary<string, List<List<string>>>();
16: 
17:         public string GetString(string key) =>
18:             _strings.TryGetValue(key, out var v) ? v : null;
19: 
20:         public float GetFloat(string key, float def = 0f) =>
21:             _strings.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
22: 
23:         public int GetInt(string key, int def = 0) =>
24:             _strings.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : def;
25: 
26:         public bool GetBool(string key) =>
27:             _strings.TryGetValue(key, out var v) && v == "true";
28: 
29:         public SimpleJsonObject GetObject(string key) =>
30:             _objects.TryGetValue(key, out var v) ? v : null;
31: 
32:         public List<string> GetArray(string key) =>
33:             _arrays.TryGetValue(key, out var v) ? v : null;
34: 
35:         public List<SimpleJsonObject> GetObjectArray(string key) =>
36:             _objectArrays.TryGetValue(key, out var v) ? v : null;
37: 
38:         /// <summary>float 배열 반환 (예: "center": [0.5, 0.3])</summary>
39:         public float[] GetFloatArray(string key)
40:         {
41:             if (_arrays.TryGetValue(key, out var arr))
42:             {
43:                 var result = new float[arr.Count];
44:                 for (int i = 0; i < arr.Count; i++)
45:                 {
46:                     if (!float.TryParse(arr[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
47:                         return null;
48:                 }
49:                 return result;
50:             }
51:             return null;
52:         }
53: 
54:         /// <summary>중첩 배열 반환 (예: "verts": [[0.3,0.7],[0.5,0.8]]). 각 내부 배열은 문자열 리스트.</summary>
55:         public List<List<string>> GetNestedArray(string key) =>
56:             _nestedArrays.TryGetValue(key, out var v) ? v : null;
57: 
58:         /// <summary>중첩 배열을 float[][] 로 변환 (예: verts)</summary>
59:         public float[][] GetNestedFloatArray(string key)
60:         {
61:             var nested = GetNestedArray(key);
62:             if (nested == null) return null;
63:             var result = new float[nested.Count][];
64:             for (int i = 0; i < nested.Count; i++)
65:             {
66:                 result[i] = new float[nested[i].Count];
67:                 for (int j = 0; j < nested[i].Count; j++)
68:                 {
69:                     if (!float.TryParse(nested[i][j], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i][j]))
70:                         return null;
71:                 }
72:             }
73:             return result;
74:         }
75: 
76:         public void SetString(string key, string value) => _strings[key] = value;
77:         public void SetObject(string key, SimpleJsonObject obj) => _objects[key] = obj;
78:         public void SetArray(string key, List<string> arr) => _arrays[key] = arr;
79:         public void SetObjectArray(string key, List<SimpleJsonObject> arr) => _objectArrays[key] = arr;
80:         public void SetNestedArray(string key, List<List<string>> arr) => _nestedArrays[key] = arr;
81:     }
82: 
83:     public static class SimpleJson
84:     {
85:         public static SimpleJsonObject Parse(string json)
86:         {
87:             int pos = 0;
88:             SkipWhitespace(json, ref pos);
89:             return ParseObject(json, ref pos);
90:         }
91: 
92:         private static SimpleJsonObject ParseObject(string json, ref int pos)
93:         {
94:             var obj = new SimpleJsonObject();
95:             if (pos >= json.Length || json[pos] != '{') return obj;
96:             pos++; // skip {
97: 
98:             while (pos < json.Length)
99:             {
100:                 SkipWhitespace(json, ref pos);
101:                 if (pos >= json.Length) break;
102:                 if (json[pos] == '}') { pos++; break; }
103:                 if (json[pos] == ',') { pos++; continue; }
104: 
105:                 // 키
106:                 var key = ParseString(json, ref pos);
107:                 SkipWhitespace(json, ref pos);
108:                 if (pos < json.Length && json[pos] == ':') pos++;
109:                 SkipWhitespace(json, ref pos);
110: 
111:                 if (pos >= json.Length) break;
112: 
113:                 // 값
114:                 if (json[pos] == '{')
115:                 {
116:                     obj.SetObject(key, ParseObject(json, ref pos));
117:                 }
118:                 else if (json[pos] == '[')
119:                 {
120:                     // 배열 내부의 첫 비-공백 문자로 타입 결정
121:                     int peekPos = pos + 1;
122:                     SkipWhitespace(json, ref peekPos);
123:                     if (peekPos < json.Length && json[peekPos] == '{')
124:                     {
125:                         obj.SetObjectArray(key, ParseObjectArray(json, ref pos));
126:                     }
127:                     else if (peekPos < json.Length && json[peekPos] == '[')
128:                     {
129:                         // 중첩 배열: [[...], [...]]
130:                         obj.SetNestedArray(key, ParseNestedArray(json, ref pos));
131:                     }
132:                     else
133:                     {
134:                         obj.SetArray(key, ParseArray(json, ref pos));
135:                     }
136:                 }
137:                 else if (json[pos] == '"')
138:                 {
139:                     obj.SetString(key, ParseString(json, ref pos));
140:                 }
141:                 else
142:                 {
143:                     // number, bool, null
144:                     var val = ParsePrimitive(json, ref pos);
145:                     obj.SetString(key, val);
146:                 }
147:             }
148:             return obj;
149:         }
150: 
151:         private static string ParseString(string json, ref int pos)
152:         {
153:             if (pos >= json.Length || json[pos] != '"') return "";
154:             pos++; // skip "
155:             var sb = new System.Text.StringBuilder();
156:             while (pos < json.Length && json[pos] != '"')
157:             {
158:                 if (json[pos] == '\\' && pos + 1 < json.Length)
159:                 {
160:                     pos++;
161:                     switch (json[pos])
162:                     {
163:                         case 'n': sb.Append('\n'); break;
164:                         case 't': sb.Append('\t'); break;
165:                         case '"': sb.Append('"'); break;
166:                         default: sb.Append(json[pos]); break;
167:                     }
168:                 }
169:                 else sb.Append(json[pos]);
170:                 pos++;
171:             }
172:             if (pos < json.Length) pos++; // skip closing "
173:             return sb.ToString();
174:         }
175: 
176:         private static List<SimpleJsonObject> ParseObjectArray(string json, ref int pos)
177:         {
178:             var list = new List<SimpleJsonObject>();
179:             if (pos >= json.Length || json[pos] != '[') return list;
180:             pos++; // skip [
```

## dev/Source/UI/SimpleJson.cs L270-296
```csharp
270:         private static void SkipValue(string json, ref int pos)
271:         {
272:             if (pos >= json.Length) return;
273:             if (json[pos] == '{') { ParseObject(json, ref pos); }
274:             else if (json[pos] == '[')
275:             {
276:                 int depth = 1;
277:                 pos++;
278:                 while (pos < json.Length && depth > 0)
279:                 {
280:                     if (json[pos] == '[') depth++;
281:                     else if (json[pos] == ']') depth--;
282:                     else if (json[pos] == '"') { ParseString(json, ref pos); continue; }
283:                     pos++;
284:                 }
285:             }
286:             else if (json[pos] == '"') { ParseString(json, ref pos); }
287:             else { ParsePrimitive(json, ref pos); }
288:         }
289: 
290:         private static void SkipWhitespace(string json, ref int pos)
291:         {
292:             while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t'))
293:                 pos++;
294:         }
295:     }
296: }
```

## dev/Source/LLM/GeminiClient.cs L1-101
```csharp
1: using System.Collections.Generic;
2: using System.Net.Http;
3: using System.Text;
4: using System.Threading.Tasks;
5: using Verse;
6: 
7: namespace MapGenAI.LLM
8: {
9:     public class GeminiClient : ILLMClient
10:     {
11:         private readonly string _apiKey;
12:         private readonly string _model;
13:         private static readonly HttpClient Http = new HttpClient();
14: 
15:         public GeminiClient(string apiKey, string model)
16:         {
17:             _apiKey = apiKey;
18:             _model = model;
19:         }
20: 
21:         public async Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt)
22:         {
23:             var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
24: 
25:             var contents = new StringBuilder();
26:             contents.Append("{\"system_instruction\":{\"parts\":[{\"text\":");
27:             contents.Append(EscapeJson(systemPrompt));
28:             contents.Append("}]},\"contents\":[");
29: 
30:             for (int i = 0; i < history.Count; i++)
31:             {
32:                 if (i > 0) contents.Append(",");
33:                 var role = history[i].Role == "assistant" ? "model" : "user";
34:                 contents.Append($"{{\"role\":\"{role}\",\"parts\":[{{\"text\":{EscapeJson(history[i].Content)}}}]}}");
35:             }
36:             // temperature 0.2: 이 LLM 작업은 "말→파라미터 추출"이라 결정론적이어야 함
37:             // (맵 다양성은 코드의 seed/noise가 만듦, LLM 온도가 아님). 고온도는 되물음·변동성 유발.
38:             // responseMimeType: 모델이 평문/마크다운 대신 항상 유효 JSON을 내도록 강제 → "말로만 됐다는데 안 바뀜" 방지.
39:             contents.Append("],\"generationConfig\":{\"temperature\":0.2,\"responseMimeType\":\"application/json\"}}");
40: 
41:             var response = await Http.PostAsync(url,
42:                 new StringContent(contents.ToString(), Encoding.UTF8, "application/json"));
43:             var json = await response.Content.ReadAsStringAsync();
44: 
45:             if (!response.IsSuccessStatusCode)
46:             {
47:                 Log.Error($"[MapGenAI] Gemini error: {json}");
48:                 throw new System.Exception($"HTTP {(int)response.StatusCode}: {json}");
49:             }
50: 
51:             // 간단한 JSON 파싱 (text 필드 추출)
52:             var textStart = json.IndexOf("\"text\": \"") + 9;
53:             var textEnd = json.IndexOf("\"", textStart);
54:             // 더 견고한 파싱은 Phase 3에서 JSON 라이브러리 추가 후 처리
55:             return ExtractText(json);
56:         }
57: 
58:         private string ExtractText(string json)
59:         {
60:             // candidates[0].content.parts[0].text 추출 (JSON 이스케이프 디코딩)
61:             var marker = "\"text\": \"";
62:             var start = json.IndexOf(marker);
63:             if (start < 0)
64:             {
65:                 // 공백 없는 형태도 대응
66:                 marker = "\"text\":\"";
67:                 start = json.IndexOf(marker);
68:                 if (start < 0) return null;
69:             }
70:             start += marker.Length;
71:             var sb = new StringBuilder();
72:             for (int i = start; i < json.Length; i++)
73:             {
74:                 if (json[i] == '\\' && i + 1 < json.Length)
75:                 {
76:                     char next = json[i + 1];
77:                     switch (next)
78:                     {
79:                         case 'n':  sb.Append('\n'); break;
80:                         case 'r':  sb.Append('\r'); break;
81:                         case 't':  sb.Append('\t'); break;
82:                         case '"':  sb.Append('"');  break;
83:                         case '\\': sb.Append('\\'); break;
84:                         default:   sb.Append(next); break;
85:                     }
86:                     i++;
87:                     continue;
88:                 }
89:                 if (json[i] == '"') break; // 문자열 종료
90:                 sb.Append(json[i]);
91:             }
92:             return sb.ToString();
93:         }
94: 
95:         private string EscapeJson(string s)
96:         {
97:             return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
98:                            .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
99:         }
100:     }
101: }
```

## dev/Source/LLM/OpenAIClient.cs L20-94
```csharp
20:             _model = model;
21:             _baseUrl = baseUrl.TrimEnd('/');
22:         }
23: 
24:         public async Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt)
25:         {
26:             var url = $"{_baseUrl}/v1/chat/completions";
27: 
28:             var messages = new StringBuilder();
29:             messages.Append($"{{\"role\":\"system\",\"content\":{EscapeJson(systemPrompt)}}}");
30:             foreach (var msg in history)
31:             {
32:                 messages.Append($",{{\"role\":\"{msg.Role}\",\"content\":{EscapeJson(msg.Content)}}}");
33:             }
34: 
35:             var body = $"{{\"model\":\"{_model}\",\"temperature\":0.7,\"messages\":[{messages}]}}";
36: 
37:             var request = new HttpRequestMessage(HttpMethod.Post, url);
38:             request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
39:             request.Content = new StringContent(body, Encoding.UTF8, "application/json");
40: 
41:             var response = await Http.SendAsync(request);
42:             var json = await response.Content.ReadAsStringAsync();
43: 
44:             if (!response.IsSuccessStatusCode)
45:             {
46:                 Log.Error($"[MapGenAI] OpenAI error: {json}");
47:                 throw new System.Exception($"HTTP {(int)response.StatusCode}: {json}");
48:             }
49: 
50:             return ExtractContent(json);
51:         }
52: 
53:         private string ExtractContent(string json)
54:         {
55:             // 공백 있는 포맷("content": "...") 과 compact 포맷("content":"...") 모두 처리
56:             string marker = "\"content\": \"";
57:             int start = json.IndexOf(marker);
58:             if (start < 0)
59:             {
60:                 marker = "\"content\":\"";
61:                 start = json.IndexOf(marker);
62:             }
63:             if (start < 0) return null;
64:             start += marker.Length;
65: 
66:             var sb = new StringBuilder();
67:             for (int i = start; i < json.Length; i++)
68:             {
69:                 if (json[i] == '\\' && i + 1 < json.Length)
70:                 {
71:                     char next = json[i + 1];
72:                     if (next == 'n')       { sb.Append('\n'); i++; }
73:                     else if (next == 'r')  { i++; }
74:                     else if (next == 't')  { sb.Append('\t'); i++; }
75:                     else if (next == '"')  { sb.Append('"');  i++; }
76:                     else if (next == '\\') { sb.Append('\\'); i++; }
77:                     else                   { sb.Append(json[i]); }
78:                 }
79:                 else if (json[i] == '"')
80:                 {
81:                     break;
82:                 }
83:                 else
84:                 {
85:                     sb.Append(json[i]);
86:                 }
87:             }
88:             return sb.ToString();
89:         }
90: 
91:         private string EscapeJson(string s)
92:         {
93:             return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
94:                            .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
```

## dev/Source/Patches/RuinDangerDensityPatch.cs L1-126
```csharp
1: using System;
2: using System.Collections.Generic;
3: using HarmonyLib;
4: using RimWorld;
5: using MapGenAI.MapGen;
6: using UnityEngine;
7: using Verse;
8: 
9: namespace MapGenAI.Patches
10: {
11:     /// <summary>
12:     /// 폐허(ScatterRuinsSimple) 및 고대 위험(ScatterShrines) 밀도 조절.
13:     /// Map Designer의 HelperMethods.ApplyBiomeSettings 방식과 동일:
14:     /// MapGenerator.GenerateMap Prefix에서 GenStepDef의 countPer10kCellsRange를 수정,
15:     /// Postfix에서 원본 복원.
16:     ///
17:     /// 폐허: density > 1일 때 3승 (Map Designer: Math.Pow(density, 3))
18:     /// 위험: density > 1일 때 4승 (Map Designer: Math.Pow(density, 4))
19:     /// </summary>
20:     [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
21:     static class Patch_RuinDangerDensity
22:     {
23:         // 원본 값 저장 (복원용)
24:         private static FloatRange _originalRuinRange;
25:         private static FloatRange _originalDangerRange;
26:         private static bool _ruinModified = false;
27:         private static bool _dangerModified = false;
28: 
29:         [HarmonyPriority(Priority.High)]
30:         static void Prefix()
31:         {
32:             _ruinModified = false;
33:             _dangerModified = false;
34: 
35:             if (!MapGenParams.HasParams) return;
36: 
37:             // 폐허 밀도
38:             if (!Mathf.Approximately(MapGenParams.RuinDensity, 1f))
39:             {
40:                 try
41:                 {
42:                     var ruinDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterRuinsSimple");
43:                     if (ruinDef?.genStep is GenStep_Scatterer ruinScatterer)
44:                     {
45:                         _originalRuinRange = ruinScatterer.countPer10kCellsRange;
46:                         _ruinModified = true;
47: 
48:                         float density = MapGenParams.RuinDensity;
49:                         if (density > 1f)
50:                             density = density * density * density; // 3승
51: 
52:                         ruinScatterer.countPer10kCellsRange.min = _originalRuinRange.min * density;
53:                         ruinScatterer.countPer10kCellsRange.max = _originalRuinRange.max * density;
54: 
55:                         Log.Message($"[MapGenAI] 폐허 밀도 적용: {MapGenParams.RuinDensity:F2} (보정={density:F2}), " +
56:                             $"range={ruinScatterer.countPer10kCellsRange.min:F1}~{ruinScatterer.countPer10kCellsRange.max:F1}");
57:                     }
58:                 }
59:                 catch (Exception e)
60:                 {
61:                     Log.Warning($"[MapGenAI] 폐허 밀도 적용 실패: {e.Message}");
62:                 }
63:             }
64: 
65:             // 위험 밀도
66:             if (!Mathf.Approximately(MapGenParams.DangerDensity, 1f))
67:             {
68:                 try
69:                 {
70:                     var dangerDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterShrines");
71:                     if (dangerDef?.genStep is GenStep_Scatterer dangerScatterer)
72:                     {
73:                         _originalDangerRange = dangerScatterer.countPer10kCellsRange;
74:                         _dangerModified = true;
75: 
76:                         float density = MapGenParams.DangerDensity;
77:                         if (density > 1f)
78:                             density = density * density * density * density; // 4승
79: 
80:                         dangerScatterer.countPer10kCellsRange.min = _originalDangerRange.min * density;
81:                         dangerScatterer.countPer10kCellsRange.max = _originalDangerRange.max * density;
82: 
83:                         Log.Message($"[MapGenAI] 위험 밀도 적용: {MapGenParams.DangerDensity:F2} (보정={density:F2}), " +
84:                             $"range={dangerScatterer.countPer10kCellsRange.min:F1}~{dangerScatterer.countPer10kCellsRange.max:F1}");
85:                     }
86:                 }
87:                 catch (Exception e)
88:                 {
89:                     Log.Warning($"[MapGenAI] 위험 밀도 적용 실패: {e.Message}");
90:                 }
91:             }
92:         }
93: 
94:         static void Postfix()
95:         {
96:             // 원본 복원 (GenStepDef는 공유 데이터이므로 반드시 복원)
97:             if (_ruinModified)
98:             {
99:                 try
100:                 {
101:                     var ruinDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterRuinsSimple");
102:                     if (ruinDef?.genStep is GenStep_Scatterer ruinScatterer)
103:                     {
104:                         ruinScatterer.countPer10kCellsRange = _originalRuinRange;
105:                     }
106:                 }
107:                 catch { }
108:                 _ruinModified = false;
109:             }
110: 
111:             if (_dangerModified)
112:             {
113:                 try
114:                 {
115:                     var dangerDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterShrines");
116:                     if (dangerDef?.genStep is GenStep_Scatterer dangerScatterer)
117:                     {
118:                         dangerScatterer.countPer10kCellsRange = _originalDangerRange;
119:                     }
120:                 }
121:                 catch { }
122:                 _dangerModified = false;
123:             }
124:         }
125:     }
126: }
```
