using System.Reflection;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests;

public sealed class DebugViewControllerTests
{
    private sealed class NoopDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private class NullProxy : DispatchProxy
    {
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
    }

    private static DebugViewActivationContext CreateContext()
    {
        ICoreClientAPI capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
        return new DebugViewActivationContext(capi, new VgeConfig());
    }

    private static DebugViewDefinition ExclusiveView(string id, NoopDisposable handle)
        => new(
            id: id,
            name: id,
            category: "Cat",
            description: string.Empty,
            registerRenderer: _ => handle,
            activationMode: DebugViewActivationMode.Exclusive);

    private static DebugViewDefinition ToggleView(string id, Func<NoopDisposable> newHandle)
        => new(
            id: id,
            name: id,
            category: "Cat",
            description: string.Empty,
            registerRenderer: _ => newHandle(),
            activationMode: DebugViewActivationMode.Toggle);

    [Fact]
    public void TryActivate_FailsBeforeInitialize()
    {
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);

        bool ok = controller.TryActivate("missing", out string? error);

        Assert.False(ok);
        Assert.Contains("not initialized", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExclusiveActivation_DisposesPreviousExclusive()
    {
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        var aHandle = new NoopDisposable();
        var bHandle = new NoopDisposable();

        registry.Register(ExclusiveView("a", aHandle));
        registry.Register(ExclusiveView("b", bHandle));

        Assert.True(controller.TryActivate("a", out _));
        Assert.Equal("a", controller.ActiveExclusiveViewId);
        Assert.False(aHandle.Disposed);

        Assert.True(controller.TryActivate("b", out _));
        Assert.Equal("b", controller.ActiveExclusiveViewId);
        Assert.True(aHandle.Disposed);
        Assert.False(bHandle.Disposed);
    }

    [Fact]
    public void ToggleActivation_TogglesOffAndDisposes()
    {
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        NoopDisposable? handle = null;
        registry.Register(ToggleView("t", () => handle = new NoopDisposable()));

        Assert.True(controller.TryActivate("t", out _));
        Assert.True(controller.IsActive("t"));
        Assert.NotNull(handle);
        Assert.False(handle!.Disposed);

        Assert.True(controller.TryActivate("t", out _));
        Assert.False(controller.IsActive("t"));
        Assert.True(handle.Disposed);
    }

    [Fact]
    public void RegistryUnregister_DeactivatesActiveViews()
    {
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        var ex = new NoopDisposable();
        NoopDisposable? toggle = null;

        registry.Register(ExclusiveView("ex", ex));
        registry.Register(ToggleView("t", () => toggle = new NoopDisposable()));

        Assert.True(controller.TryActivate("ex", out _));
        Assert.True(controller.TryActivate("t", out _));

        Assert.Equal("ex", controller.ActiveExclusiveViewId);
        Assert.True(controller.IsActive("t"));

        Assert.True(registry.Unregister("ex"));
        Assert.True(ex.Disposed);
        Assert.Null(controller.ActiveExclusiveViewId);

        Assert.True(registry.Unregister("t"));
        Assert.NotNull(toggle);
        Assert.True(toggle!.Disposed);
        Assert.False(controller.IsActive("t"));
    }
}
