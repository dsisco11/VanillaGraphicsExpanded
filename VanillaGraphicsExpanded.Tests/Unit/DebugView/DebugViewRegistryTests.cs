using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.DebugView;

namespace VanillaGraphicsExpanded.Tests.Unit.DebugView;

/// <summary>Checks duplicate registration handling and deterministic debug-view ordering.</summary>
public sealed class DebugViewRegistryTests
{
    #region Public API
    /// <summary>A duplicate identifier is rejected and emits one diagnostic warning.</summary>
    [Fact]
    public void Register_RejectsDuplicateIds()
    {
        // Arrange
        var warnings = new List<string>();
        var registry = new DebugViewRegistry
        {
            LogWarning = warnings.Add
        };

        // Act
        bool first = registry.Register(View("v1", "A", "Cat"));
        bool duplicate = registry.Register(View("v1", "B", "Cat"));

        // Assert
        Assert.True(first);
        Assert.False(duplicate);
        Assert.Single(warnings);
        Assert.Contains("duplicate id", warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Views are ordered by category, then display name.</summary>
    [Fact]
    public void GetAll_SortsByCategoryThenName()
    {
        // Arrange
        var registry = new DebugViewRegistry();

        registry.Register(View("2", "B", "PBR"));
        registry.Register(View("1", "A", "PBR"));
        registry.Register(View("3", "C", "LumOn"));

        // Act
        DebugViewDefinition[] all = registry.GetAll();

        // Assert
        Assert.Equal(new[] { "3", "1", "2" }, Array.ConvertAll(all, v => v.Id));
    }

    /// <summary>Case-insensitive category and name ties are resolved by identifier.</summary>
    [Fact]
    public void GetAll_SortsByCategoryThenNameThenId_CaseInsensitive()
    {
        // Arrange
        var registry = new DebugViewRegistry();

        registry.Register(View("b", "Same", "pbr"));
        registry.Register(View("A", "same", "PBR"));

        // Act
        DebugViewDefinition[] all = registry.GetAll();

        // Assert
        Assert.Equal(new[] { "A", "b" }, Array.ConvertAll(all, v => v.Id));
    }
    #endregion

    #region Private
    /// <summary>Creates a definition that can be registered without initializing a renderer.</summary>
    private static DebugViewDefinition View(string id, string name, string category)
        => new(
            id: id,
            name: name,
            category: category,
            description: string.Empty,
            registerRenderer: _ => new NoopDisposable());

    /// <summary>Provides an inert renderer handle for registry-only scenarios.</summary>
    private sealed class NoopDisposable : IDisposable
    {
        #region Public API
        /// <summary>Releases no resources.</summary>
        public void Dispose() { }
        #endregion
    }
    #endregion
}
