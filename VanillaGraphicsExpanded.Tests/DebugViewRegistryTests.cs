using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.DebugView;

namespace VanillaGraphicsExpanded.Tests;

public sealed class DebugViewRegistryTests
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
    public void Register_RejectsDuplicateIds()
    {
        var warnings = new List<string>();
        var registry = new DebugViewRegistry
        {
            LogWarning = warnings.Add
        };

        Assert.True(registry.Register(View("v1", "A", "Cat")));
        Assert.False(registry.Register(View("v1", "B", "Cat")));

        Assert.Single(warnings);
        Assert.Contains("duplicate id", warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAll_SortsByCategoryThenName()
    {
        var registry = new DebugViewRegistry();

        registry.Register(View("2", "B", "PBR"));
        registry.Register(View("1", "A", "PBR"));
        registry.Register(View("3", "C", "LumOn"));

        DebugViewDefinition[] all = registry.GetAll();

        Assert.Equal(new[] { "3", "1", "2" }, Array.ConvertAll(all, v => v.Id));
    }
}

