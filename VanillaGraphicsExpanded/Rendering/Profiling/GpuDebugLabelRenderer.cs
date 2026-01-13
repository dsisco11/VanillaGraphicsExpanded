using System.Collections.Generic;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Profiling;

/// <summary>
/// Wraps all Vintage Story render stages with GPU debug label markers.
/// Helps identify render passes in GPU profiling tools like RenderDoc, Nsight, or PIX.
/// </summary>
/// <remarks>
/// This renderer registers at the beginning and end of each stage to wrap all rendering
/// with push/pop debug groups. The groups are visible in GPU profilers and help identify
/// which operations belong to which render stage.
/// </remarks>
internal sealed class GpuDebugLabelRenderer : IRenderer
{
    private static readonly Dictionary<EnumRenderStage, GlDebug.GroupScope> ActiveScopes = new();
    
    private readonly ICoreClientAPI capi;
    private readonly bool isEnd;
    
    public double RenderOrder { get; }
    
    public int RenderRange => 0;

    public GpuDebugLabelRenderer(ICoreClientAPI capi, bool isEnd)
    {
        this.capi = capi;
        this.isEnd = isEnd;
        RenderOrder = isEnd ? 999.0 : -999.0;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (isEnd)
        {
            // Pop debug group (close the scope) that was stored by the begin renderer
            if (ActiveScopes.TryGetValue(stage, out var scope))
            {
                scope.Dispose();
                ActiveScopes.Remove(stage);
            }
        }
        else
        {
            // Push debug group for this render stage and store it
            string stageName = GetStageName(stage);
            var scope = GlDebug.Group(stageName);
            ActiveScopes[stage] = scope;
        }
    }

    /// <summary>
    /// Gets a descriptive name for the render stage.
    /// </summary>
    private static string GetStageName(EnumRenderStage stage)
    {
        return stage switch
        {
            EnumRenderStage.Before => "VS.Before",
            EnumRenderStage.Opaque => "VS.Opaque",
            EnumRenderStage.OIT => "VS.OIT",
            EnumRenderStage.AfterOIT => "VS.AfterOIT",
            EnumRenderStage.ShadowFar => "VS.ShadowFar",
            EnumRenderStage.ShadowFarDone => "VS.ShadowFarDone",
            EnumRenderStage.ShadowNear => "VS.ShadowNear",
            EnumRenderStage.ShadowNearDone => "VS.ShadowNearDone",
            EnumRenderStage.AfterPostProcessing => "VS.AfterPostProcessing",
            EnumRenderStage.AfterBlit => "VS.AfterBlit",
            EnumRenderStage.Ortho => "VS.Ortho",
            EnumRenderStage.AfterFinalComposition => "VS.AfterFinalComposition",
            EnumRenderStage.Done => "VS.Done",
            _ => $"VS.Unknown{(int)stage}"
        };
    }

    public void Dispose()
    {
        // Clean up any remaining scopes in case of early disposal
        if (!isEnd)
        {
            foreach (var scope in ActiveScopes.Values)
            {
                scope.Dispose();
            }
            ActiveScopes.Clear();
        }
    }
}

/// <summary>
/// Manages registration of GPU debug label renderers for all render stages.
/// </summary>
internal static class GpuDebugLabelManager
{
    private static readonly List<GpuDebugLabelRenderer> Renderers = new();
    
    /// <summary>
    /// Registers debug label renderers for all render stages.
    /// </summary>
    public static void Register(ICoreClientAPI capi)
    {
#if DEBUG
        capi.Logger.Notification("[VGE] Registering GPU debug label renderers for all render stages");

        // Define all render stages to wrap
        EnumRenderStage[] stages = new[]
        {
            EnumRenderStage.Before,
            EnumRenderStage.Opaque,
            EnumRenderStage.OIT,
            EnumRenderStage.AfterOIT,
            EnumRenderStage.ShadowFar,
            EnumRenderStage.ShadowFarDone,
            EnumRenderStage.ShadowNear,
            EnumRenderStage.ShadowNearDone,
            EnumRenderStage.AfterPostProcessing,
            EnumRenderStage.AfterBlit,
            EnumRenderStage.Ortho,
            EnumRenderStage.AfterFinalComposition,
            EnumRenderStage.Done
        };

        // Register begin (push) and end (pop) renderers for each stage
        foreach (var stage in stages)
        {
            var beginRenderer = new GpuDebugLabelRenderer(capi, isEnd: false);
            var endRenderer = new GpuDebugLabelRenderer(capi, isEnd: true);
            
            capi.Event.RegisterRenderer(beginRenderer, stage, $"vge_debug_begin_{stage}");
            capi.Event.RegisterRenderer(endRenderer, stage, $"vge_debug_end_{stage}");
            
            Renderers.Add(beginRenderer);
            Renderers.Add(endRenderer);
        }

        capi.Logger.Notification("[VGE] GPU debug label renderers registered successfully");
#endif
    }

    /// <summary>
    /// Unregisters all debug label renderers.
    /// </summary>
    public static void Unregister(ICoreClientAPI capi)
    {
#if DEBUG
        if (Renderers.Count > 0)
        {
            capi.Logger.Notification("[VGE] Unregistering GPU debug label renderers");

            EnumRenderStage[] stages = new[]
            {
                EnumRenderStage.Before,
                EnumRenderStage.Opaque,
                EnumRenderStage.OIT,
                EnumRenderStage.AfterOIT,
                EnumRenderStage.ShadowFar,
                EnumRenderStage.ShadowFarDone,
                EnumRenderStage.ShadowNear,
                EnumRenderStage.ShadowNearDone,
                EnumRenderStage.AfterPostProcessing,
                EnumRenderStage.AfterBlit,
                EnumRenderStage.Ortho,
                EnumRenderStage.AfterFinalComposition,
                EnumRenderStage.Done
            };

            foreach (var renderer in Renderers)
            {
                foreach (var stage in stages)
                {
                    capi.Event.UnregisterRenderer(renderer, stage);
                }
                renderer.Dispose();
            }

            Renderers.Clear();
        }
#endif
    }
}
