using System.Collections.Generic;
using Verse;

namespace MapGenAI.UI
{
    /// <summary>
    /// 인라인 한/영 번역 시스템.
    /// RimWorld의 Languages/Keyed 시스템 대신 코드 내장 방식 사용 (로딩 안정성).
    /// </summary>
    public static class L10n
    {
        public static bool IsKorean()
        {
            try { return Prefs.LangFolderName == "Korean" || Prefs.LangFolderName == "Korean (한국어)"; }
            catch { return false; }
        }

        private static readonly Dictionary<string, (string ko, string en)> Strings = new Dictionary<string, (string ko, string en)>
        {
            // UI
            {"MapGenAI_Title", ("MapGen AI", "MapGen AI")},
            {"MapGenAI_Button", ("✦ AI 맵 생성", "✦ AI Map Gen")},
            {"MapGenAI_Send", ("전송", "Send")},
            {"MapGenAI_PresetSave", ("프리셋 저장", "Save Preset")},
            {"MapGenAI_PresetLoad", ("프리셋 불러오기", "Load Preset")},
            {"MapGenAI_Generate", ("이 설정으로 맵 생성", "Generate with these settings")},
            {"MapGenAI_PresetNamePrompt", ("프리셋 이름을 입력하세요:", "Enter preset name:")},
            {"MapGenAI_Save", ("저장", "Save")},
            {"MapGenAI_Cancel", ("취소", "Cancel")},

            // Messages
            {"MapGenAI_Error", ("[오류] {0}", "[Error] {0}")},
            {"MapGenAI_NoResponse", ("응답 없음. API 키를 확인해주세요.", "No response. Please check your API key.")},
            {"MapGenAI_ParamsSaved", ("MapGen AI: 파라미터 저장 완료. 맵을 시작하면 적용됩니다.", "MapGen AI: Parameters saved. They will be applied when you start the map.")},
            {"MapGenAI_NoPresets", ("저장된 프리셋이 없습니다.", "No saved presets.")},
            {"MapGenAI_PresetSavedMsg", ("프리셋 \"{0}\" 이(가) 저장되었습니다.", "Preset \"{0}\" has been saved.")},
            {"MapGenAI_PresetDeletedMsg", ("프리셋 \"{0}\" 이(가) 삭제되었습니다.", "Preset \"{0}\" has been deleted.")},
            {"MapGenAI_PresetLoadFailed", ("프리셋 \"{0}\" 불러오기에 실패했습니다.", "Failed to load preset \"{0}\".")},
            {"MapGenAI_ModifyHint", ("아래 버튼으로 맵을 생성하거나, 채팅으로 수정할 수 있습니다.", "Press the button below to generate, or modify via chat.")},
            {"MapGenAI_DialogCancelled", ("대화 취소 — 파라미터 리셋, 미리보기 원래대로", "Dialog cancelled — parameters reset, preview restored")},
            {"MapGenAI_Requesting", ("요청 중...", "Requesting...")},
            {"MapGenAI_Waiting", ("생성 중", "Generating")},
            {"MapGenAI_ParamsSet", ("맵 파라미터가 설정되었습니다.", "Map parameters have been set.")},

            // Welcome
            {"MapGenAI_Welcome", (
                "맵을 자연어로 설명해 보세요!\n\n예시:\n  - \"대각선 산맥에 중앙 호수\"\n  - \"온천이 있는 산악 요새\"\n  - \"강을 일자로, 왼쪽에 배치\"\n  - \"대리석으로만 된 깨끗한 평지\"\n  - \"그냥 추천해줘\"",
                "Describe your ideal map in natural language!\n\nExamples:\n  - \"Diagonal mountain range with central lake\"\n  - \"Mountain fortress with hot springs\"\n  - \"Straight river on the left side\"\n  - \"Clean flatland with marble only\"\n  - \"Just recommend something\""
            )},

            // Preset loaded
            {"MapGenAI_PresetLoadedMsg", (
                "프리셋 \"{0}\" 을(를) 불러왔습니다.\n언덕={1}, 산양={2}, 나무={3}, 동물={4}, 강={5}, 동굴={6}, 간헐천={7}",
                "Loaded preset \"{0}\".\nHills={1}, Amount={2}, Trees={3}, Animals={4}, River={5}, Caves={6}, Geysers={7}"
            )},

            // Tile context
            {"MapGenAI_Unknown", ("알 수 없음", "Unknown")},
            {"MapGenAI_RiverNone", ("없음", "None")},
            {"MapGenAI_RiverPresent", ("있음", "Present")},
            {"MapGenAI_RiverOceanLink", (" (바다/호수로 연결됨)", " (connected to ocean/lake)")},
            {"MapGenAI_Yes", ("예", "Yes")},
            {"MapGenAI_No", ("아니오", "No")},

            // Mutator labels
            {"MapGenAI_MutatorLabel", ("{0}(표시명:{1}{2})", "{0}(label:{1}{2})")},
            {"MapGenAI_OdysseyRequired", ("  (특수 지형 변형은 Odyssey DLC가 필요합니다. Odyssey 없이는 호수/온천/피요르드 등 mutator를 사용할 수 없습니다.)", "  (Special terrain features require the Odyssey DLC. Without Odyssey, mutators like lakes/hot springs/fjords are unavailable.)")},
            {"MapGenAI_NoMutatorsAvailable", ("  (이 타일에서 사용 가능한 특수 지형 변형 없음)", "  (No special terrain features available for this tile)")},
            {"MapGenAI_MutatorLoadFailed", ("  (mutator 목록 로드 실패)", "  (Failed to load mutator list)")},

            // Validation warnings
            {"MapGenAI_MutatorNotFound", ("[!] '{0}'은(는) 존재하지 않는 지형 변형이어서 제거되었습니다.", "[!] '{0}' does not exist and was removed.")},
            {"MapGenAI_MutatorCoastalOnly", ("[!] [{0}]은(는) 해안 타일에서만 가능하여 제거되었습니다.", "[!] [{0}] is only available on coastal tiles and was removed.")},
            {"MapGenAI_MutatorRiverOnly", ("[!] [{0}]은(는) 강이 있는 타일에서만 가능하여 제거되었습니다.", "[!] [{0}] is only available on tiles with rivers and was removed.")},
        };

        /// <summary>키로 번역 문자열 조회 (포맷 인수 없음)</summary>
        public static string Tr(this string key)
        {
            if (Strings.TryGetValue(key, out var pair))
                return IsKorean() ? pair.ko : pair.en;
            return key;
        }

        /// <summary>키로 번역 문자열 조회 + string.Format 적용</summary>
        public static string Tr(this string key, params object[] args)
        {
            string template = key.Tr();
            try { return string.Format(template, args); }
            catch { return template; }
        }
    }
}
