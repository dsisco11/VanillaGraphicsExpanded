using System.Reflection;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Component.DebugView;

/// <summary>Checks availability and active-toggle selection through the debug-view controller boundary.</summary>
public sealed class DebugViewerCategoryTests
{
    #region Public API
    /// <summary>Direct PBR diagnostics remain selectable when indirect lighting is disabled.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PbrViewIsAvailableIndependentlyOfLumOn(bool enabled)
    {
        // Arrange
        var context = CreateContext();
        context.Config.LumOn.Enabled = enabled;

        // Act: locate the private factory used by built-in debug view registration.
        var factory = typeof(VgeBuiltInDebugViews).GetMethod("CreatePbrDebugView", BindingFlags.Static | BindingFlags.NonPublic);

        // Assert
        Assert.NotNull(factory);

        // Act / Assert: invoke the factory, confirm its definition type, and evaluate availability.
        var view = Assert.IsType<DebugViewDefinition>(factory.Invoke(null, null));
        Assert.True(view.GetAvailability(context).IsAvailable);
    }

    /// <summary>No active toggles produce an empty active-toggle selection.</summary>
    [Fact]
    public void GetActiveToggleViewIds_ReturnsEmpty_WhenNoTogglesActive()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        // Act
        string[] activeIds = controller.GetActiveToggleViewIds();

        // Assert
        Assert.Empty(activeIds);
    }

    /// <summary>Activating a toggle includes its identifier in the selected toggle list.</summary>
    [Fact]
    public void GetActiveToggleViewIds_ReturnsToggleIds_WhenTogglesActive()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        registry.Register(ToggleView("t1"));

        // Act / Assert: activate the toggle and verify its identifier appears in the active selection.
        Assert.True(controller.TryActivate("t1", out _));

        Assert.Equal(new[] { "t1" }, controller.GetActiveToggleViewIds());
    }
    #endregion

    #region Private
    #region Helpers
    /// <summary>Creates the activation context with an inert client API boundary.</summary>
    private static DebugViewActivationContext CreateContext()
    {
        ICoreClientAPI capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
        return new DebugViewActivationContext(capi, new VgeConfig());
    }

    /// <summary>Creates a toggle view whose renderer registration owns a no-op disposable.</summary>
    private static DebugViewDefinition ToggleView(string id)
        => new(
            id: id,
            name: id,
            category: "Cat",
            description: string.Empty,
            registerRenderer: _ => new NoopDisposable(),
            activationMode: DebugViewActivationMode.Toggle);

    /// <summary>Supplies an inert renderer handle for controller activation tests.</summary>
    private sealed class NoopDisposable : IDisposable
    {
        #region Public API
        /// <summary>Releases no resources.</summary>
        public void Dispose() { }
        #endregion
    }

    /// <summary>Returns default values for client API members unused by these tests.</summary>
    private class NullProxy : DispatchProxy
    {
        #region Protected
        /// <summary>Supplies null or default values without creating engine objects.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void))
            {
                return null;
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
        #endregion
    }
    #endregion
    #endregion
}
