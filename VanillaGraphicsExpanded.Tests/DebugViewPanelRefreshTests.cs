using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using Vintagestory.API.Client;
using static VanillaGraphicsExpanded.DebugView.VgeBuiltInDebugViews;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Exercises mode-dependent panel refresh requests without initializing native GUI drawing.</summary>
public sealed class DebugViewPanelRefreshTests
{
    #region Mode transitions

    /// <summary>Every actual selection change requests fresh controls, including legend-to-legend changes.</summary>
    [Theory]
    [InlineData("ScreenProbeGrid", "NearFieldGeometry")]
    [InlineData("NearFieldGeometry", "ScreenProbeGrid")]
    [InlineData("NearFieldGeometry", "ProbeAtlasTraceOutcome")]
    [InlineData("ProbeAtlasTraceOutcome", "ProbeAtlasTemporalRejection")]
    [InlineData("WorldProbeSuppressedLighting", "WorldProbeLightingEffect")]
    [InlineData("WorldProbeLightingEffect", "WorldProbeSuppressedLighting")]
    [InlineData("ScreenProbeGrid", "WorldProbeImportance")]
    [InlineData("WorldProbeImportance", "ScreenProbeGrid")]
    public void ChangedMode_RequestsFreshLayout_WithoutActivatingRenderer(string previous, string next)
    {
        using var fixture = new PanelFixture(previous);
        int refreshes = 0;
        fixture.Panel.LayoutChanged += () => refreshes++;

        fixture.Select(next);

        Assert.Equal(1, refreshes);
        Assert.Equal(next, ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault().ToString());
        Assert.Equal(LumOnDebugMode.Off, fixture.Config.LumOn.DebugMode);
        Assert.Null(DebugViewController.Instance.ActiveExclusiveViewId);
    }

    /// <summary>Redundant, malformed, and unselected dropdown notifications leave the layout unchanged.</summary>
    [Theory]
    [InlineData("ScreenProbeGrid", true)]
    [InlineData("NearFieldGeometry", false)]
    [InlineData("not-a-mode", true)]
    [InlineData("99999", true)]
    public void IgnoredSelection_DoesNotRequestLayout(string code, bool selected)
    {
        using var fixture = new PanelFixture("ScreenProbeGrid");
        int refreshes = 0;
        fixture.Panel.LayoutChanged += () => refreshes++;

        fixture.Select(code, selected);

        Assert.Equal(0, refreshes);
        Assert.Equal(ProbeVizMode.ScreenProbeGrid, ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault());
        Assert.Equal(LumOnDebugMode.Off, fixture.Config.LumOn.DebugMode);
    }

    /// <summary>Refreshing the active panel preserves renderer switching and persisted mode selection.</summary>
    [Fact]
    public void ActiveModeChange_RequestsLayoutAndPersistsRendererMode()
    {
        using var fixture = new PanelFixture("ScreenProbeGrid");
        Assert.True(DebugViewController.Instance.TryActivate(fixture.Definition.Id, out _));
        int refreshes = 0;
        fixture.Panel.LayoutChanged += () => refreshes++;

        fixture.Select("WorldProbeLightingEffect");

        Assert.Equal(1, refreshes);
        Assert.Equal(LumOnDebugMode.WorldProbeLightingEffect, fixture.Config.LumOn.DebugMode);
        Assert.Equal(LumOnDebugMode.WorldProbeLightingEffect, fixture.Config.Debug.DebugViews.ActiveExclusiveLumOnDebugMode);
        Assert.Equal(fixture.Definition.Id, DebugViewController.Instance.ActiveExclusiveViewId);
    }

    /// <summary>A layout rebuild retains the explicitly inspected panel while another view renders.</summary>
    [Theory]
    [InlineData("inspected", "inspected")]
    [InlineData(null, "active")]
    [InlineData("removed", "active")]
    public void RestoreSelection_PreservesExistingPanelOrFallsBackToActive(string? selected, string expected)
    {
        var capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
        var registry = new DebugViewRegistry();
        using var controller = new DebugViewController(registry);
        controller.Initialize(new DebugViewActivationContext(capi, new VgeConfig()));
        var active = new DebugViewDefinition("active", "Active", "Test", "", _ => new EmptyHandle());
        var inspected = new DebugViewDefinition("inspected", "Inspected", "Test", "", _ => new EmptyHandle());
        registry.Register(active);
        registry.Register(inspected);
        Assert.True(controller.TryActivate("active", out _));

        // This method only uses selection state; bypass native GUI construction for the focused contract.
        var dialog = (GuiDialogVgeDebugViewer)RuntimeHelpers.GetUninitializedObject(typeof(GuiDialogVgeDebugViewer));
        typeof(GuiDialogVgeDebugViewer).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, controller);
        FieldInfo selection = typeof(GuiDialogVgeDebugViewer).GetField("selectedViewId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        selection.SetValue(dialog, selected);
        typeof(GuiDialogVgeDebugViewer).GetMethod("RestoreSelectedViewId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [new[] { inspected, active }]);

        Assert.Equal(expected, selection.GetValue(dialog));
    }

    /// <summary>Queued layout work is deferred and discarded after the dialog closes or changes panels.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeferredRefresh_IgnoresClosedOrReplacedPanel(bool closeDialog)
    {
        using var fixture = new PanelFixture("ScreenProbeGrid");
        var events = DispatchProxy.Create<IClientEventAPI, NullProxy>();
        Action? queued = null;
        ((NullProxy)(object)events).Handler = (method, args) =>
        {
            if (method?.Name == "EnqueueMainThreadTask") queued = (Action)args![0]!;
            return null;
        };
        var capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
        ((NullProxy)(object)capi).Handler = (method, _) => method?.Name == "get_Event" ? events : null;
        var dialog = (GuiDialogVgeDebugViewer)RuntimeHelpers.GetUninitializedObject(typeof(GuiDialogVgeDebugViewer));
        FieldInfo opened = typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo panel = typeof(GuiDialogVgeDebugViewer).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        typeof(GuiDialog).GetField("capi", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, capi);
        opened.SetValue(dialog, true);
        panel.SetValue(dialog, fixture.Panel);

        // With no native composer, an immediate or stale rebuild would throw instead of returning.
        typeof(GuiDialogVgeDebugViewer).GetMethod("OnPanelLayoutChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, null);
        Assert.NotNull(queued);
        if (closeDialog) opened.SetValue(dialog, false);
        else panel.SetValue(dialog, null);
        queued();
    }
    #endregion

    #region Fixtures

    /// <summary>Owns the real registered panel and restores process-wide debug state after each case.</summary>
    private sealed class PanelFixture : IDisposable
    {
        private readonly ProbeVizMode previousMode = ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault();
        public VgeConfig Config { get; } = new();
        public DebugViewDefinition Definition { get; }
        public IDebugViewPanel Panel { get; }

        /// <summary>Registers the production panel factory with a client API that does no rendering.</summary>
        public PanelFixture(string initialMode)
        {
            var capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
            Config.LumOn.Enabled = true;
            Config.LumOn.DebugMode = LumOnDebugMode.Off;
            DebugViewController.Instance.DeactivateAll();
            DebugViewController.Instance.Initialize(new DebugViewActivationContext(capi, Config));
            Definition = (DebugViewDefinition)typeof(VgeBuiltInDebugViews)
                .GetMethod("CreateProbesDebugView", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            DebugViewRegistry.Instance.Register(Definition);
            Assert.True(DebugViewRegistry.Instance.TryGet(Definition.Id, out var registered));
            ProbesDebugViewState.Instance.SetSelectedProbeVizMode(Enum.Parse<ProbeVizMode>(initialMode));
            Panel = registered!.CreatePanel!(new DebugViewActivationContext(capi, Config))!;
        }

        /// <summary>Delivers a dropdown notification to the real panel callback.</summary>
        public void Select(string code, bool selected = true)
        {
            Panel.GetType().GetMethod("OnModeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Panel, [code, selected]);
        }

        /// <summary>Removes the owned registration and restores the prior mode.</summary>
        public void Dispose()
        {
            Panel.Dispose();
            DebugViewController.Instance.DeactivateAll();
            DebugViewRegistry.Instance.Unregister(Definition.Id);
            ProbesDebugViewState.Instance.SetSelectedProbeVizMode(previousMode);
        }
    }

    /// <summary>Provides harmless default API returns without creating engine objects.</summary>
    private class NullProxy : DispatchProxy
    {
        public Func<MethodInfo?, object?[]?, object?>? Handler { get; set; }
        /// <summary>Returns default values for unused API members.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Handler is not null) return Handler(targetMethod, args);
            Type? result = targetMethod?.ReturnType;
            return result is not null && result != typeof(void) && result.IsValueType ? Activator.CreateInstance(result) : null;
        }
    }

    /// <summary>Provides a renderer lifetime for selection-only tests.</summary>
    private sealed class EmptyHandle : IDisposable
    {
        /// <summary>Releases no resources.</summary>
        public void Dispose() { }
    }

    #endregion
}


