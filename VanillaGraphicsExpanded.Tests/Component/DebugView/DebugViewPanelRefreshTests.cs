using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using Vintagestory.API.Client;
using static VanillaGraphicsExpanded.DebugView.VgeBuiltInDebugViews;

namespace VanillaGraphicsExpanded.Tests.Component.DebugView;

/// <summary>Exercises mode-dependent panel refresh requests without initializing native GUI drawing.</summary>
[Collection("CPU state")]
public sealed class DebugViewPanelRefreshTests
{
    #region Public API
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
        // Arrange
        using var fixture = new PanelFixture(previous);
        int refreshes = 0;
        fixture.Panel.LayoutChanged += () => refreshes++;

        // Act
        fixture.Select(next);

        // Assert
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
        // Arrange
        using var fixture = new PanelFixture("ScreenProbeGrid");
        int refreshes = 0;
        fixture.Panel.LayoutChanged += () => refreshes++;

        // Act
        fixture.Select(code, selected);

        // Assert
        Assert.Equal(0, refreshes);
        Assert.Equal(ProbeVizMode.ScreenProbeGrid, ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault());
        Assert.Equal(LumOnDebugMode.Off, fixture.Config.LumOn.DebugMode);
    }

    /// <summary>Refreshing the active panel preserves renderer switching and persisted mode selection.</summary>
    [Fact]
    public void ActiveModeChange_RequestsLayoutAndPersistsRendererMode()
    {
        // Arrange
        using var fixture = new PanelFixture("ScreenProbeGrid");
        Assert.True(DebugViewController.Instance.TryActivate(fixture.Definition.Id, out _));
        int refreshes = 0;
        fixture.Panel.LayoutChanged += () => refreshes++;

        // Act
        fixture.Select("WorldProbeLightingEffect");

        // Assert
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
        // Arrange
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
        // Act
        typeof(GuiDialogVgeDebugViewer).GetMethod("RestoreSelectedViewId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [new[] { inspected, active }]);

        // Assert
        Assert.Equal(expected, selection.GetValue(dialog));
    }

    /// <summary>Queued layout work is deferred and discarded after the dialog closes or changes panels.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeferredRefresh_IgnoresClosedOrReplacedPanel(bool closeDialog)
    {
        // Arrange
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
        // Act: queue a refresh while the panel is current.
        typeof(GuiDialogVgeDebugViewer).GetMethod("OnPanelLayoutChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, null);

        // Assert that refresh was deferred until the main-thread callback.
        Assert.NotNull(queued);

        // Arrange the stale-panel state for the queued callback.
        if (closeDialog) opened.SetValue(dialog, false);
        else panel.SetValue(dialog, null);

        // Act / Assert: invoking stale queued work must safely return.
        queued();
    }
    #endregion

    #endregion

    #region Private
    #region Helpers

    /// <summary>Owns the real registered panel and restores process-wide debug state after each case.</summary>
    private sealed class PanelFixture : IDisposable
    {
        #region Public API
        public VgeConfig Config { get; } = new();
        public DebugViewDefinition Definition { get; }
        public IDebugViewPanel Panel => panel ?? throw new InvalidOperationException("The debug panel was not created.");

        /// <summary>Registers the production panel factory with a client API that does no rendering.</summary>
        public PanelFixture(string initialMode)
        {
            try
            {
                var capi = DispatchProxy.Create<ICoreClientAPI, NullProxy>();
                Config.LumOn.Enabled = true;
                Config.LumOn.DebugMode = LumOnDebugMode.Off;
                controller.DeactivateAll();
                controller.Initialize(new DebugViewActivationContext(capi, Config));
                Definition = (DebugViewDefinition)typeof(VgeBuiltInDebugViews)
                    .GetMethod("CreateProbesDebugView", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                if (registry.Register(Definition))
                {
                    ownedDefinition = Definition;
                }
                else
                {
                    Assert.True(registry.TryGet(Definition.Id, out var existing));
                    Assert.NotNull(existing);
                    Definition = existing!;
                }

                ProbesDebugViewState.Instance.SetSelectedProbeVizMode(Enum.Parse<ProbeVizMode>(initialMode));
                panel = Definition.CreatePanel!(new DebugViewActivationContext(capi, Config))!;
            }
            catch
            {
                RestoreState();
                throw;
            }
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
            if (disposed) return;
            disposed = true;
            try
            {
                panel?.Dispose();
            }
            finally
            {
                RestoreState();
            }
        }

        #endregion

        #region Private
        private readonly ProbeVizMode previousMode = ProbesDebugViewState.Instance.GetSelectedProbeVizModeOrDefault();
        private readonly DebugViewRegistry registry = DebugViewRegistry.Instance;
        private readonly DebugViewController controller = DebugViewController.Instance;
        private DebugViewDefinition? ownedDefinition;
        private IDebugViewPanel? panel;
        private bool disposed;

        /// <summary>Releases partial fixture state while preserving registrations that the fixture did not create.</summary>
        private void RestoreState()
        {
            try
            {
                controller.DeactivateAll();
            }
            finally
            {
                try
                {
                    if (ownedDefinition is not null) registry.Unregister(ownedDefinition.Id);
                }
                finally
                {
                    ProbesDebugViewState.Instance.SetSelectedProbeVizMode(previousMode);
                }
            }
        }
        #endregion
    }

    /// <summary>Provides harmless default API returns without creating engine objects.</summary>
    private class NullProxy : DispatchProxy
    {
        #region Public API
        public Func<MethodInfo?, object?[]?, object?>? Handler { get; set; }
        #endregion

        #region Protected
        /// <summary>Returns default values for unused API members.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Handler is not null) return Handler(targetMethod, args);
            Type? result = targetMethod?.ReturnType;
            return result is not null && result != typeof(void) && result.IsValueType ? Activator.CreateInstance(result) : null;
        }
        #endregion
    }

    /// <summary>Provides a renderer lifetime for selection-only tests.</summary>
    private sealed class EmptyHandle : IDisposable
    {
        #region Public API
        /// <summary>Releases no resources.</summary>
        public void Dispose() { }
        #endregion
    }

    #endregion
    #endregion
}


