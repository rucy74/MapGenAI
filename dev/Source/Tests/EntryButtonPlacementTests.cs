using MapGenAI;
using static CoreRegressionTests;
using Box = MapGenAI.EntryButtonPlacement.Box;

// The world-map entry button must land where no window covers it.
static class EntryButtonPlacementTests
{
    // Measured in the isolated game at 1920x1080, UI scale 1.5 (1280x720 UI units), Map Preview 1.12.26 defaults.
    static readonly Box Toolbar = new Box(980, 50, 250, 50);
    static readonly Box PreviewWindow = new Box(980, 105, 250, 250);
    static readonly Box InspectPane = new Box(0, 520, 432, 165);

    static string Text(Box b) => b.X + "," + b.Y + "," + b.W + "," + b.H;

    public static void RunAll()
    {
        Check("Default Map Preview layout: the button goes left of the toolbar, clear of the preview window", () =>
        {
            var spot = EntryButtonPlacement.Choose(Toolbar, 1280, 720, new[] { Toolbar, PreviewWindow, InspectPane }, 110, 30, 5);
            Equal("865,60,110,30", Text(spot));
            Equal(false, spot.Overlaps(PreviewWindow));
        });

        Check("The old spot below the toolbar is inside the preview window", () =>
        {
            Equal(true, new Box(980, 105, 110, 30).Overlaps(PreviewWindow));
        });

        Check("With room on the right the button stays right of the toolbar", () =>
        {
            var toolbar = new Box(600, 50, 250, 50);
            Equal("855,60,110,30", Text(EntryButtonPlacement.Choose(toolbar, 1280, 720, new[] { toolbar, new Box(600, 105, 250, 250) }, 110, 30, 5)));
        });

        Check("Toolbar at the left edge keeps the button on its right", () =>
        {
            var toolbar = new Box(20, 50, 250, 50);
            Equal("275,60,110,30", Text(EntryButtonPlacement.Choose(toolbar, 1280, 720, new[] { toolbar }, 110, 30, 5)));
        });

        Check("Left spot covered and no preview window open: the button goes below the toolbar", () =>
        {
            var chat = new Box(300, 40, 670, 600);
            Equal("980,105,110,30", Text(EntryButtonPlacement.Choose(Toolbar, 1280, 720, new[] { Toolbar, chat }, 110, 30, 5)));
        });

        Check("Every on-screen spot covered (chat open on the left): the first on-screen spot is kept", () =>
        {
            var chat = new Box(300, 40, 670, 600);
            Equal("865,60,110,30", Text(EntryButtonPlacement.Choose(Toolbar, 1280, 720, new[] { Toolbar, PreviewWindow, chat }, 110, 30, 5)));
        });

        Check("Toolbar open: the button is placed beside the toolbar, as before", () =>
        {
            Equal(Text(Toolbar), Text(EntryButtonPlacement.Anchor(Toolbar, PreviewWindow, 1280, 30)));
        });

        Check("Toolbar turned off in Map Preview: the button goes left of the preview window, level with its top", () =>
        {
            var spot = EntryButtonPlacement.Choose(EntryButtonPlacement.Anchor(null, PreviewWindow, 1280, 30), 1280, 720, new[] { PreviewWindow, InspectPane }, 110, 30, 5);
            Equal("865,105,110,30", Text(spot));
            Equal(false, spot.Overlaps(PreviewWindow));
        });

        Check("Toolbar off and the preview window dragged to the left edge: the button goes right of it", () =>
        {
            var preview = new Box(10, 200, 250, 250);
            Equal("265,200,110,30", Text(EntryButtonPlacement.Choose(EntryButtonPlacement.Anchor(null, preview, 1280, 30), 1280, 720, new[] { preview }, 110, 30, 5)));
        });

        Check("Toolbar off and no preview window (an impassable tile): the button goes to the top-right corner", () =>
        {
            Equal("1160,5,110,30", Text(EntryButtonPlacement.Choose(EntryButtonPlacement.Anchor(null, null, 1280, 30), 1280, 720, new[] { InspectPane }, 110, 30, 5)));
        });
    }
}
