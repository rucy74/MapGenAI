using MapGenAI.LLM;
using MapGenAI.UI;
using static CoreRegressionTests;

static class RecommendationControlsTests
{
    public static void RunAll()
    {
        Check("Discard phrases are local and do not swallow map edits", () =>
        {
            foreach (var text in new[] { "추천 취소", "추천 모두 취소해 줘", "선택 안 함", "아무것도 선택 안 할래", "None of these.", "Select none", "cancel the suggestions" })
                Equal(true, RecommendationPlan.IsDismissal(text));
            foreach (var text in new[] { "추천 취소하고 북쪽에 산 만들어줘", "추천 말고 호수 만들어줘", "cancel the options and add a lake", "1번", "2번을 자연스럽게 해 줘", "다시 추천해 줘" })
                Equal(false, RecommendationPlan.IsDismissal(text));
        });
        Check("New suggestion requests leave the pending candidate editor", () =>
        {
            foreach (var text in new[] { "다시 추천해 줘", "다른 걸 추천해줘", "다 마음에 안 들어. 새로운 후보로 추천해줘", "다른 선택지 보여줘", "New suggestions", "Recommend different options for my current map.", "suggest again", "more ideas please" })
                Equal(true, RecommendationPlan.RequestsNewOptions(text));
            foreach (var text in new[] { "3번에서 다른 걸 추천해줘", "Suggest different options for candidate 2", "1번", "추천 취소", "추천 말고 호수 만들어줘", "북쪽에 산 추가", "Make option 3 more natural" })
                Equal(false, RecommendationPlan.RequestsNewOptions(text));
        });
        Check("Recommendation layout prioritizes chat and keeps footer inside window", () =>
        {
            // Interior sizes: default, 720p, UI scaling, and the old dialog.
            foreach (var height in new[] { 724f, 644f, 484f, 400f })
            foreach (var width in new[] { 864f, 584f })
            foreach (var count in new[] { 0, 1, 2, 3 })
            {
                var layout = new RecommendationLayout(width, height, count, false);
                Equal(true, layout.ChatHeight >= (height < 422f ? 218f : 240f));
                Equal(height, 32f + layout.ChatHeight + layout.ChoiceHeight + 86f);
                Equal(true, layout.PreviewHeight == 0f || layout.PreviewHeight >= 98f);
                if (count == 0) Equal(0f, layout.ChoiceHeight);
            }
        });
        Check("Folding gives preview space back to chat while keeping option controls", () =>
        {
            var shown = new RecommendationLayout(864, 724, 3, false);
            var folded = new RecommendationLayout(864, 724, 3, true);
            Equal(0f, folded.PreviewHeight);
            Equal(64f, folded.ChoiceHeight);
            Equal(shown.ChatHeight + shown.PreviewHeight, folded.ChatHeight);
        });
    }
}
