using System;

namespace MapGenAI.UI
{
    // Heights inside the window margin. Keep chat usable before allocating preview space.
    public sealed class RecommendationLayout
    {
        public readonly float ChatHeight, PreviewHeight, ChoiceHeight;
        public RecommendationLayout(float width, float height, int count, bool collapsed)
        {
            const float titleAndFooter = 32f + 86f;
            if (count > 0)
            {
                float available = height - titleAndFooter - 64f - 240f;
                float preview = Math.Min(178f, Math.Min((width - 6f * (count - 1)) / count + 28f, available));
                PreviewHeight = !collapsed && preview >= 98f ? preview : 0f;
                ChoiceHeight = PreviewHeight + 64f;
            }
            ChatHeight = Math.Max(40f, height - titleAndFooter - ChoiceHeight);
        }
    }
}
