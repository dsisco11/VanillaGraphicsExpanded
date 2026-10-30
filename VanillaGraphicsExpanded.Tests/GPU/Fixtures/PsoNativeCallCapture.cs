using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Records native state effects separately from setup, draw application and engine restoration.</summary>
internal sealed partial class PsoNativeCallCapture : IDisposable
{
    private static readonly FieldInfo selectedGraphics = typeof(RHICommandContext).GetField("selectedGraphicsPipeline", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHICommandContext).FullName, "selectedGraphicsPipeline");
    private static readonly FieldInfo pipelineLifetime = typeof(RHIGraphicsPipeline).GetField("lifetime", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHIGraphicsPipeline).FullName, "lifetime");
    private static readonly FieldInfo referencedPipeline = typeof(RHIGraphicsPipelineRef).GetField("pipeline", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHIGraphicsPipelineRef).FullName, "pipeline");
    private readonly RHICommandContext context = RHICommandContext.Current;
    private readonly OpenGlStateCache cache = OpenGlStateCache.Current;
    private RHIGraphicsPipeline? markedPipeline;
    internal readonly List<string> Events = [];

    #region Public API
    /// <summary>Installs native observers without depending on JIT-sensitive managed method patching.</summary>
    internal PsoNativeCallCapture()
    {
        InstallNativeObservers();
    }

    /// <summary>Retires observers before any inspection or fixture cleanup.</summary>
    public void Dispose()
    {
        native.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Reads existing execution ownership solely for fixture trace classification, without adding a production hook.</summary>
    private RHIGraphicsPipeline? ExecutingPipeline()
    {
        // Idle selections can reference a retired owner. Inspect only its execution marker;
        // observation must not invoke the reference's validation or native resource admission.
        var pipeline = (RHIGraphicsPipeline?)referencedPipeline.GetValue(selectedGraphics.GetValue(context)!);
        if (pipeline is null) return null;
        // BeginSubmission owns value one through native dispatch and cleanup; zero is idle and two retired.
        return (int)pipelineLifetime.GetValue(pipeline)! == 1 ? pipeline : null;
    }

    /// <summary>Associates subsequent native effects with the exact selected immutable blend contract.</summary>
    private void MarkApplication(RHIGraphicsPipeline pipeline)
    {
        if (ReferenceEquals(markedPipeline, pipeline)) return;
        markedPipeline = pipeline;
        var description = pipeline.Description;
        Events.Add("draw:" + string.Join(",", description.Blending.Select((blend, slot) =>
            description.Targets.Colors[slot].Format is null ? "hole" : blend.Enabled ? "blend" : "replace")));
    }

    /// <summary>Labels native effects from current execution facts while excluding pass loads and boundary restoration.</summary>
    private string Segment()
    {
        if (cache.RestoringBoundary) { markedPipeline = null; return "restoration"; }
        if (ExecutingPipeline() is { } pipeline) { MarkApplication(pipeline); return "application"; }
        markedPipeline = null;
        return "setup";
    }

    /// <summary>Checks real blend state at each native draw rather than relying on a managed postfix surviving warm-up.</summary>
    private void DrawCompleted()
    {
        var pipeline = ExecutingPipeline() ?? throw new InvalidOperationException("A captured RHI draw has no executing pipeline.");
        MarkApplication(pipeline);
        if (pipeline.Description.Blending.Any(blend=>blend.Enabled))
        {
            // Check native values independently of the emitted-call log.
            var state=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(PsoNativeStateFingerprint.Read())!;
            for(int slot=0;slot<pipeline.Description.Blending.Count;slot++)
            {
                var blend=pipeline.Description.Blending[slot];
                if(!blend.Enabled || pipeline.Description.Targets.Colors[slot].Format is null)continue;
                Assert.Equal(((int)OpenGLBlendValues.SourceFactor(blend.SourceRgb)).ToString(),state["BlendSrcRgb:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.DestinationFactor(blend.DestinationRgb)).ToString(),state["BlendDstRgb:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.SourceFactor(blend.SourceAlpha)).ToString(),state["BlendSrcAlpha:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.DestinationFactor(blend.DestinationAlpha)).ToString(),state["BlendDstAlpha:"+slot]);
            }
            Events.Add("validated:active-blend-factors");
        }
        markedPipeline = null;
    }
    #endregion
}
