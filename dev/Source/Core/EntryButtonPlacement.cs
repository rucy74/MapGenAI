using System.Collections.Generic;

namespace MapGenAI
{
    /// <summary>
    /// Picks where the world-map entry button goes. The button is drawn with the world interface, underneath every
    /// window. Map Preview keeps its toolbar near the right screen edge and its preview window right below the toolbar,
    /// so the old "right of the toolbar, else below it" rule always ended up under the preview window and the button
    /// could not be seen. Spots that overlap an open window are now skipped.
    /// </summary>
    public static class EntryButtonPlacement
    {
        public struct Box
        {
            public float X, Y, W, H;
            public Box(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
            public float XMax => X + W;
            public float YMax => Y + H;
            public bool Overlaps(Box other) => X < other.XMax && other.X < XMax && Y < other.YMax && other.Y < YMax;
        }

        /// <summary>
        /// Right of the toolbar, then left of it, then below it: the first spot that is on screen and clear of every
        /// window. If every on-screen spot is covered, the first on-screen one; if none is on screen, below the toolbar.
        /// </summary>
        public static Box Choose(Box toolbar, float screenWidth, float screenHeight, IEnumerable<Box> windows,
            float width, float height, float gap, float margin = 5f)
        {
            float midY = toolbar.Y + (toolbar.H - height) / 2f;
            var below = new Box(toolbar.X, toolbar.YMax + gap, width, height);
            var candidates = new[]
            {
                new Box(toolbar.XMax + gap, midY, width, height),
                new Box(toolbar.X - gap - width, midY, width, height),
                below,
            };
            var open = new List<Box>(windows);
            Box? firstOnScreen = null;
            foreach (var spot in candidates)
            {
                if (spot.X < margin || spot.Y < margin || spot.XMax > screenWidth - margin || spot.YMax > screenHeight - margin) continue;
                if (firstOnScreen == null) firstOnScreen = spot;
                bool covered = false;
                foreach (var window in open)
                    if (spot.Overlaps(window)) { covered = true; break; }
                if (!covered) return spot;
            }
            return firstOnScreen ?? below;
        }
    }
}
