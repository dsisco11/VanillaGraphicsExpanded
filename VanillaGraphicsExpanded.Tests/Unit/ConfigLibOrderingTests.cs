using Newtonsoft.Json.Linq;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Protects the shared ordering namespace used by ConfigLib's settings and formatting combiner.</summary>
[Trait("Category", "Unit")]
public sealed class ConfigLibOrderingTests
{
    #region Public API
    /// <summary>Every typed setting and formatting block must fit into one sorted map without duplicate weights.</summary>
    [Fact]
    public void SettingsAndFormattingHaveUniqueWeights()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json");
        var asset = JObject.Parse(File.ReadAllText(path));
        var blocks = new SortedDictionary<float, string>();
        // ConfigLib merges all setting categories with formatting, rather than giving
        // boolean, integer and float settings separate ordering namespaces.
        foreach (var category in asset["settings"]!.Children<JProperty>())
        foreach (var setting in category.Value.Children<JProperty>())
            Add(blocks, setting.Value["weight"]!.Value<float>(), category.Name + "/" + setting.Name);
        foreach (var block in asset["formatting"]!)
            Add(blocks, block["weight"]!.Value<float>(), "formatting/" + block["title"]!.Value<string>());
    }
    #endregion

    #region Private
    /// <summary>Reports both conflicting owners before the same insertion that fails during ConfigLib loading.</summary>
    private static void Add(SortedDictionary<float, string> blocks, float weight, string owner)
    {
        Assert.True(float.IsFinite(weight), $"Nonfinite ConfigLib weight: {owner}");
        Assert.False(blocks.TryGetValue(weight, out string? previous),
            $"ConfigLib weight {weight} is shared by {previous} and {owner}.");
        blocks.Add(weight, owner);
    }
    #endregion
}
