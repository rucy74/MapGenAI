using System;
using UnityEngine;
using Verse;

namespace MapGenAI.UI
{
    public sealed class Dialog_RecommendationPreview : Window
    {
        readonly RecommendationPreviews.Item item;
        readonly int number;
        readonly Func<bool> valid;
        readonly Action select;
        public override Vector2 InitialSize => new Vector2(Math.Min(780f, Verse.UI.screenWidth - 50f), Math.Min(830f, Verse.UI.screenHeight - 40f));
        public Dialog_RecommendationPreview(RecommendationPreviews.Item item, int number, Func<bool> valid, Action select)
        {
            this.item = item; this.number = number; this.valid = valid; this.select = select;
            doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; layer = WindowLayer.Super;
        }
        public override void DoWindowContents(Rect rect)
        {
            if (!valid() || item.Texture == null) { Close(false); return; }
            bool ko = L10n.IsKorean();
            Widgets.Label(new Rect(0, 0, rect.width - 25, 28), ko ? number + "번 미리보기" : "Option " + number + " preview");
            GUI.DrawTexture(new Rect(0, 32, rect.width, rect.height - 128), item.Texture, ScaleMode.ScaleToFit, false);
            var note = new Rect(0, rect.height - 90, rect.width, 48);
            Widgets.Label(note, ko ? "지형과 배치를 비교하는 미리보기입니다. 식물·건물 내부·적은 실제 생성 때 달라질 수 있습니다." :
                "Terrain and layout preview. Plants, building interiors and occupants can differ in the generated map.");
            if (!string.IsNullOrEmpty(item.Warning)) TooltipHandler.TipRegion(note, item.Warning);
            if (Widgets.ButtonText(new Rect(0, rect.height - 36, rect.width, 36), ko ? number + "번 적용" : "Apply option " + number))
            { Close(false); select(); }
        }
    }
}
