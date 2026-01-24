using VanillaGraphicsExpanded.DebugView;

namespace VanillaGraphicsExpanded.Tests;

public sealed class DebugViewerCategoryTests
{
    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private static DebugViewDefinition View(string id, string name, string category)
        => new(
            id: id,
            name: name,
            category: category,
            description: string.Empty,
            registerRenderer: _ => new NoopDisposable());

    [Fact]
    public void BuildCategoryList_ReturnsAll_WhenNoViews()
    {
        string[] cats = GuiDialogVgeDebugViewer.BuildCategoryList([], "All");
        Assert.Equal(new[] { "All" }, cats);
    }

    [Fact]
    public void BuildCategoryList_DedupesCaseInsensitively_AndSortsWithAllFirst()
    {
        DebugViewDefinition[] all =
        [
            View("1", "A", "LumOn"),
            View("2", "B", "pbr"),
            View("3", "C", "lumon"),
        ];

        string[] cats = GuiDialogVgeDebugViewer.BuildCategoryList(all, "All");

        Assert.Equal(new[] { "All", "LumOn", "pbr" }, cats);
    }
}

