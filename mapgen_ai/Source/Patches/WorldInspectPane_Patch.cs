using System.Runtime.CompilerServices;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using MapGenAI.UI;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// Dialog_TextToMap이 열려 있는 동안 Map Preview 미리보기 창이 닫히지 않도록 보호.
    ///
    /// 근본 원인: Enter 키 입력 시 IMGUI 이벤트 전파 메커니즘에 의해
    /// WorldSelector.selectedTile이 일시적으로 Invalid가 될 수 있음.
    /// Event.current.Use()로 이벤트를 소비해도 GUI.Window 간
    /// 포커스 관리/이벤트 재배포가 독립적으로 작동하여 방지 불가.
    ///
    /// 2단계 방어:
    ///
    /// 1단계 -- UpdateWhileWorldShown Prefix:
    ///   selectedTile이 해제된 경우 저장된 유효 타일로 복원.
    ///   Map Preview가 타일 변경을 감지하지 않아 Close() 자체가 호출 안 됨.
    ///
    /// 2단계 -- WindowStack.TryRemove Prefix:
    ///   1단계를 우회하여 Close()가 호출되더라도,
    ///   MapPreviewWindow/MapPreviewToolbar의 제거를 차단하는 안전망.
    /// </summary>
    [HarmonyPatch]
    static class Patch_MapPreview_UpdateWhileWorldShown
    {
        private static PlanetTile _savedTile = PlanetTile.Invalid;

        private static readonly System.Reflection.FieldInfo _selectedTileField =
            typeof(WorldSelector).GetField("selectedTile",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static bool Prepare()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "MapPreviewMod")
                    return true;
            }
            Log.Message("[MapGenAI] MapPreview not found, skipping UpdateWhileWorldShown patch.");
            return false;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return typeof(MapPreview.WorldInterfaceManager)
                .GetMethod("UpdateWhileWorldShown",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
        }

        /// <summary>
        /// UpdateWhileWorldShown 직전에 selectedTile 필드를 복원.
        /// 이 시점은 Map Preview가 필드를 읽기 직전이므로, 가장 효과적.
        /// 복원된 값은 유지되어 같은 프레임의 다른 코드에도 유효한 타일이 보임.
        /// </summary>
        static void Prefix(WorldSelector selector)
        {
            if (_selectedTileField == null || selector == null) return;

            var currentTile = (PlanetTile)_selectedTileField.GetValue(selector);

            if (Find.WindowStack.IsOpen<Dialog_TextToMap>())
            {
                if (currentTile >= 0)
                {
                    _savedTile = currentTile;
                }
                else if (_savedTile >= 0)
                {
                    _selectedTileField.SetValue(selector, _savedTile);
                }
            }
            else
            {
                _savedTile = PlanetTile.Invalid;
            }
        }
    }

    [HarmonyPatch(typeof(WorldInterface), "WorldInterfaceOnGUI")]
    public static class WorldInterface_Patch
    {
        private const float BtnW = 110f;
        private const float BtnH = 30f;
        private const float Gap = 5f;

        // 타일 변경 감지용
        private static int _lastTileId = -1;

        // Map Preview 재오픈용 필드
        private static readonly System.Reflection.FieldInfo _selectedTileField =
            typeof(WorldSelector).GetField("selectedTile",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsMapPreviewWindowOpen()
        {
            return MapPreview.MapPreviewWindow.Instance != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ForceReopenPreview()
        {
            // selectedTile 필드 복원 + Map Preview TileId 리셋 → 다음 프레임에 재오픈
            if (_selectedTileField != null && _lastTileId >= 0)
                _selectedTileField.SetValue(Find.WorldSelector, (PlanetTile)_lastTileId);
            MapPreview.WorldInterfaceManager.RefreshPreview();
        }

        private static bool _checkedMapPreview = false;
        private static bool _mapPreviewLoaded = false;

        private static bool IsMapPreviewLoaded()
        {
            if (!_checkedMapPreview)
            {
                _checkedMapPreview = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MapPreview")
                    {
                        _mapPreviewLoaded = true;
                        break;
                    }
                }
            }
            return _mapPreviewLoaded;
        }

        // Map Preview 툴바 위치 가져오기 (NoInlining: TypeLoadException 방지)
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Rect? GetToolbarRect()
        {
            var toolbar = MapPreview.MapPreviewToolbar.Instance;
            return toolbar != null ? (Rect?)toolbar.windowRect : null;
        }

        static void Postfix()
        {
            try
            {
                int currentTile = Find.WorldSelector.SelectedTile;

                // 채팅창 열려있고 타일이 해제됐으면 → 복원 + Map Preview 재오픈
                if (currentTile < 0 && Find.WindowStack.IsOpen<Dialog_TextToMap>()
                    && _lastTileId >= 0 && IsMapPreviewLoaded())
                {
                    ForceReopenPreview();
                    return;
                }

                if (currentTile < 0) return;

                // 타일 변경 감지 → 파라미터 리셋 + Map Preview 원래대로
                if (_lastTileId != currentTile)
                {
                    if (_lastTileId >= 0 && MapGen.MapGenParams.HasParams)
                    {
                        MapGen.MapGenParams.Reset();
                        MapGen.MapGenParams.RefreshMapPreview();
                    }
                    _lastTileId = currentTile;
                }

                // AI 버튼은 MapPreviewToolbarButton에서 툴바에 등록됨 — 별도 그리기 불필요
                // (툴바 등록 실패 시 fallback)
                if (!MapPreviewToolbarButton.IsRegistered && IsMapPreviewLoaded())
                {
                    var toolbarRect = GetToolbarRect();
                    if (toolbarRect != null)
                    {
                        var tr = toolbarRect.Value;
                        var btnRect = new Rect(tr.xMax + Gap, tr.y + (tr.height - BtnH) / 2f, BtnW, BtnH);
                        if (btnRect.xMax > Verse.UI.screenWidth - 5f)
                            btnRect = new Rect(tr.x, tr.yMax + Gap, BtnW, BtnH);
                        if (Widgets.ButtonText(btnRect, "✦ AI 맵 생성"))
                            Find.WindowStack.Add(new Dialog_TextToMap());
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error($"[MapGenAI] WorldInterface Postfix 오류: {e}");
            }
        }
    }

    /// <summary>[DebugAction] 인게임 빠른 테스트 버튼</summary>
    public static class TextToMap_DebugActions
    {
        [DebugAction("TextToMap", "대화창 열기", allowedGameStates = AllowedGameStates.Playing)]
        static void OpenDialog()
        {
            Find.WindowStack.Add(new Dialog_TextToMap());
        }

        [DebugAction("TextToMap", "API 연결 테스트", allowedGameStates = AllowedGameStates.Playing)]
        static void TestAPI()
        {
            var client = LLM.LLMClientFactory.Create(MapGenAIMod.Settings);
            System.Threading.Tasks.Task.Run(async () =>
            {
                var response = await client.SendChatAsync(
                    new System.Collections.Generic.List<LLM.ChatMessage>
                    {
                        new LLM.ChatMessage("user", "안녕하세요, API 연결 테스트입니다. 한 문장으로 응답해주세요.")
                    },
                    "당신은 RimWorld 맵 생성 도우미입니다.");
                LongEventHandler.ExecuteWhenFinished(() =>
                    Log.Message($"[MapGenAI] API 응답: {response}"));
            });
        }

        [DebugAction("TextToMap", "현재 파라미터 출력", allowedGameStates = AllowedGameStates.Playing)]
        static void PrintParams()
        {
            if (!MapGen.MapGenParams.HasParams)
            {
                Log.Message("[MapGenAI] 저장된 파라미터 없음");
                return;
            }
            Log.Message($"[MapGenAI] 파라미터: 언덕={MapGen.MapGenParams.Hills}, " +
                        $"나무={MapGen.MapGenParams.VegetationDensity}, " +
                        $"강={MapGen.MapGenParams.HasRiver}, " +
                        $"도로={MapGen.MapGenParams.HasRoads}");
        }

        [DebugAction("TextToMap", "미리보기 캐시 초기화", allowedGameStates = AllowedGameStates.Playing)]
        static void ClearPreviewCache()
        {
            MapPreviewIntegration.ClearCache();
            Log.Message("[MapGenAI] 미리보기 캐시 초기화 완료");
        }
    }
}
