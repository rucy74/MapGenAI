using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MapGenAI.UI;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// Map Preview 툴바에 AI 버튼을 리플렉션으로 등록.
    /// MapPreview.MapPreviewToolbar.Button을 직접 상속하면
    /// TypeLoadException이 발생하므로 (어셈블리 로드 시점),
    /// 리플렉션 + DynamicMethod로 우회.
    ///
    /// 등록 실패 시 fallback: WorldInspectPane_Patch에서 텍스트 버튼으로 표시.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MapPreviewToolbarButton
    {
        public static bool IsRegistered { get; private set; } = false;

        static MapPreviewToolbarButton()
        {
            bool found = false;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "MapPreviewMod")
                { found = true; break; }
            }
            if (found)
            {
                try { TryRegister(); }
                catch (Exception e) { Log.Warning($"[MapGenAI] 툴바 버튼 등록 실패: {e.Message}"); }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TryRegister()
        {
            // MapPreviewToolbar.Button은 abstract class — 리플렉션으로 서브클래스를 만들 수 없으므로
            // 등록 자체를 포기하고 fallback(텍스트 버튼)에 의존.
            // Map Preview 툴바 옆에 텍스트 버튼이 표시됨.
            Log.Message("[MapGenAI] Map Preview 감지됨 — 툴바 옆 텍스트 버튼 사용");
            IsRegistered = false;
        }
    }
}
