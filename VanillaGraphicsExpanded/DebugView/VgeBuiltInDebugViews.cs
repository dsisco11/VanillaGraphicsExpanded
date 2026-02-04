using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using System;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private const string CategoryProbes = "Probes";
    private const string CategoryPbr = "PBR";
    private const string CategoryMotion = "Motion";
    private const string CategoryProfiling = "Profiling";
    private const string CategoryGBuffer = "GBuffer";
    private const string CategoryGeometry = "Geometry";
    private const string CategoryScenes = "Scenes";
    private const string CategoryTools = "Tools";

    private const string ProbesDebugViewId = "vge.lumon.probes";
    private const string PbrDebugViewId = "vge.lumon.pbr";
    private const string GBufferDebugViewId = "vge.lumon.gbuffers";
    private const string MotionDebugViewId = "vge.lumon.motion";
    private const string GpuProfilerViewId = "vge.profiling.gpu";
    private const string GpuDebugGroupsViewId = "vge.profiling.gpuDebugGroups";
    private const string GBufferOverlayViewId = "vge.gbuffer.overlay";
    private const string ToolsViewId = "vge.tools";
    private const string ArtifactsViewId = "vge.pbr.artifacts";
    private const string WorldCellBoundsViewId = "vge.geometry.worldCellBounds";
    private const string TraceSceneClipmapViewId = "vge.lumon.tracescene.clipmap";

    public static void RegisterAll(
        ICoreClientAPI capi,
        GBufferManager gBufferManager)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));
        if (gBufferManager is null) throw new ArgumentNullException(nameof(gBufferManager));

        DebugViewRegistry.Instance.LogWarning ??= msg => capi.Logger.Warning(msg);
        DebugViewController.Instance.LogWarning ??= msg => capi.Logger.Warning(msg);

        DebugViewRegistry.Instance.Register(CreateProbesDebugView());
        DebugViewRegistry.Instance.Register(CreatePbrDebugView());
        DebugViewRegistry.Instance.Register(CreateGBufferDebugView());
        DebugViewRegistry.Instance.Register(CreateMotionDebugView());
        DebugViewRegistry.Instance.Register(CreateGpuProfilerView());
        DebugViewRegistry.Instance.Register(CreateGpuDebugGroupsView());
        DebugViewRegistry.Instance.Register(CreateGBufferOverlayView(gBufferManager));
        DebugViewRegistry.Instance.Register(CreateWorldCellBoundsView());
        DebugViewRegistry.Instance.Register(CreateTraceSceneClipmapDebugView());
        DebugViewRegistry.Instance.Register(CreateToolsView());
        DebugViewRegistry.Instance.Register(CreateArtifactsView());
    }
}
