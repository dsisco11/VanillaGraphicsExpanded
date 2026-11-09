using System.Reflection;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Component.DebugView;

/// <summary>Checks activation, toggle, unregister, and persistence behavior across the debug-view controller and registry.</summary>
public sealed class DebugViewControllerTests
{
    #region Public API
    /// <summary>Activation fails with a clear diagnostic until the controller receives its activation context.</summary>
    [Fact]
    public void TryActivate_FailsBeforeInitialize()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);

        // Act
        bool activated = controller.TryActivate("missing", out string? error);

        // Assert
        Assert.False(activated);
        Assert.Contains("not initialized", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Activating another exclusive view disposes the prior renderer and retains the new one.</summary>
    [Fact]
    public void ExclusiveActivation_DisposesPreviousExclusive()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());
        var aHandle = new NoopDisposable();
        var bHandle = new NoopDisposable();
        registry.Register(ExclusiveView("a", aHandle));
        registry.Register(ExclusiveView("b", bHandle));

        // Act
        bool activatedA = controller.TryActivate("a", out _);

        // Assert
        Assert.True(activatedA);
        Assert.Equal("a", controller.ActiveExclusiveViewId);
        Assert.False(aHandle.Disposed);

        // Act
        bool activatedB = controller.TryActivate("b", out _);

        // Assert
        Assert.True(activatedB);
        Assert.Equal("b", controller.ActiveExclusiveViewId);
        Assert.True(aHandle.Disposed);
        Assert.False(bHandle.Disposed);
    }

    /// <summary>Activating an active toggle again turns it off and disposes its renderer.</summary>
    [Fact]
    public void ToggleActivation_TogglesOffAndDisposes()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());
        NoopDisposable? handle = null;
        registry.Register(ToggleView("t", () => handle = new NoopDisposable()));

        // Act
        bool activated = controller.TryActivate("t", out _);

        // Assert
        Assert.True(activated);
        Assert.True(controller.IsActive("t"));
        Assert.NotNull(handle);
        Assert.False(handle!.Disposed);

        // Act
        bool toggledOff = controller.TryActivate("t", out _);

        // Assert
        Assert.True(toggledOff);
        Assert.False(controller.IsActive("t"));
        Assert.True(handle.Disposed);
    }

    /// <summary>Unregistering active exclusive and toggle views deactivates them and disposes their renderer handles.</summary>
    [Fact]
    public void RegistryUnregister_DeactivatesActiveViews()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());
        var exclusiveHandle = new NoopDisposable();
        NoopDisposable? toggleHandle = null;
        registry.Register(ExclusiveView("ex", exclusiveHandle));
        registry.Register(ToggleView("t", () => toggleHandle = new NoopDisposable()));
        Assert.True(controller.TryActivate("ex", out _));
        Assert.True(controller.TryActivate("t", out _));
        Assert.Equal("ex", controller.ActiveExclusiveViewId);
        Assert.True(controller.IsActive("t"));

        // Act
        bool exclusiveUnregistered = registry.Unregister("ex");

        // Assert
        Assert.True(exclusiveUnregistered);
        Assert.True(exclusiveHandle.Disposed);
        Assert.Null(controller.ActiveExclusiveViewId);

        // Act
        bool toggleUnregistered = registry.Unregister("t");

        // Assert
        Assert.True(toggleUnregistered);
        Assert.NotNull(toggleHandle);
        Assert.True(toggleHandle!.Disposed);
        Assert.False(controller.IsActive("t"));
    }

    /// <summary>Changing the active LumOn mode persists the selected debug-view identity and mode together.</summary>
    [Fact]
    public void NotifyExclusiveModeChanged_PersistsCurrentLumOnMode()
    {
        // Arrange
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        var context = CreateContext();
        controller.Initialize(context);
        registry.Register(ExclusiveView("ex", new NoopDisposable()));
        Assert.True(controller.TryActivate("ex", out _));
        context.Config.LumOn.DebugMode = LumOnDebugMode.WorldProbeLightingEffect;

        // Act
        controller.NotifyExclusiveModeChanged();

        // Assert
        Assert.Equal("ex", context.Config.Debug.DebugViews.ActiveExclusiveViewId);
        Assert.Equal(LumOnDebugMode.WorldProbeLightingEffect, context.Config.Debug.DebugViews.ActiveExclusiveLumOnDebugMode);
    }
    #endregion

    #region Private
    #region Helpers
    /// <summary>Creates an activation context with a client API proxy that supplies inert defaults.</summary>
    private static DebugViewActivationContext CreateContext()
    {
        ICoreClientAPI capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
        return new DebugViewActivationContext(capi, new VgeConfig());
    }

    /// <summary>Creates an exclusive view that returns the supplied renderer handle.</summary>
    private static DebugViewDefinition ExclusiveView(string id, NoopDisposable handle)
        => new(
            id: id,
            name: id,
            category: "Cat",
            description: string.Empty,
            registerRenderer: _ => handle,
            activationMode: DebugViewActivationMode.Exclusive);

    /// <summary>Creates a toggle view whose renderer handle is supplied when activated.</summary>
    private static DebugViewDefinition ToggleView(string id, Func<NoopDisposable> newHandle)
        => new(
            id: id,
            name: id,
            category: "Cat",
            description: string.Empty,
            registerRenderer: _ => newHandle(),
            activationMode: DebugViewActivationMode.Toggle);

    /// <summary>Tracks whether controller cleanup released a renderer handle.</summary>
    private sealed class NoopDisposable : IDisposable
    {
        #region Public API
        /// <summary>Indicates whether disposal occurred.</summary>
        public bool Disposed { get; private set; }

        /// <summary>Records release of this controlled renderer lifetime.</summary>
        public void Dispose()
        {
            Disposed = true;
        }
        #endregion
    }

    /// <summary>Provides default values for unused client API members without constructing engine objects.</summary>
    private class NullProxy : DispatchProxy
    {
        #region Protected
        /// <summary>Returns null or a default value based on the requested API member's return type.</summary>
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
