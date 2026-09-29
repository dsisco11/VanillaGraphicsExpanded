using System.Reflection;

using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests;

public sealed class DebugViewerActiveSelectionTests
{
    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
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

    private static DebugViewDefinition ToggleView(string id)
        => new(
            id: id,
            name: id,
            category: "Cat",
            description: string.Empty,
            registerRenderer: _ => new NoopDisposable(),
            activationMode: DebugViewActivationMode.Toggle);

    /// <summary>Direct PBR diagnostics remain selectable when indirect lighting is disabled.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PbrViewIsAvailableIndependentlyOfLumOn(bool enabled)
    {
        var context = CreateContext();
        context.Config.LumOn.Enabled = enabled;
        var factory = typeof(VgeBuiltInDebugViews).GetMethod("CreatePbrDebugView", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(factory);
        var view = Assert.IsType<DebugViewDefinition>(factory.Invoke(null, null));
        Assert.True(view.GetAvailability(context).IsAvailable);
    }

    [Fact]
    public void GetActiveToggleViewIds_ReturnsEmpty_WhenNoTogglesActive()
    {
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        Assert.Empty(controller.GetActiveToggleViewIds());
    }

    [Fact]
    public void GetActiveToggleViewIds_ReturnsToggleIds_WhenTogglesActive()
    {
        var registry = new DebugViewRegistry();
        var controller = new DebugViewController(registry);
        controller.Initialize(CreateContext());

        registry.Register(ToggleView("t1"));

        Assert.True(controller.TryActivate("t1", out _));
        Assert.Equal(new[] { "t1" }, controller.GetActiveToggleViewIds());
    }
}
